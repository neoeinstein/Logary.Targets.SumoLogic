namespace Logary.Targets

open Hopac
open Hopac.Infixes
open Hopac.Extensions
open HttpFs.Client
open Logary
open Logary.Target
open Logary.Internals
open System

[<AutoOpen>]
module Prelude =
  let inline (^) x = x

type SumoLogicConf =
  {
    endpoint : Uri
    templateHandling : TemplateHandlingConf
    batchSize : uint16
    maxRecoveryAttempts : uint32
    timeoutMs : uint32
  }
and TemplateHandlingConf =
  | ExpandTemplates
  | IgnoreTemplates

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SumoLogicConf =
  let empty =
    { endpoint = Uri "http://localhost/"
      templateHandling = IgnoreTemplates
      batchSize = 100us
      maxRecoveryAttempts = 3u
      timeoutMs = 500u
    }

  let create endpoint templateHandling =
    { empty with
        endpoint = endpoint
        templateHandling = templateHandling }

module Serialization =
  open Logary.Utils.Chiron
  open Logary.Utils.Chiron.Operators

  module Json =
    let inline writeMixin (a : ^a) =
      (^a : (static member ToJson: ^a -> Json<unit>) a)

  let rec valueToJson (x:Value) =
    match x with
    | Value.Array vs -> vs |> List.map valueToJson |> Json.Array
    | Value.Object m -> m |> Map.map (fun _ -> valueToJson) |> Json.Object
    | Value.BigInt bi -> Json.Number (decimal bi)
    | Value.Bool b -> Json.Bool b
    | Value.Float f -> Json.Number (decimal f)
    | Value.Int64 i -> Json.Number (decimal i)
    | Value.String str -> Json.String str
    | Value.Binary _
    | Value.Fraction _ -> Json.Object Map.empty |> Json.writeMixin x |> snd

  let unitsToString (x:Units) =
    let str = Units.symbol x
    if String.startsWith "(" str && str.EndsWith ")" then
      str.Substring (1, String.length str - 2)
    else
      str

  let fieldlikeToJson v uO =
      match uO with
      | None ->
        valueToJson v
      | Some u ->
        Json.Object ^ Map.ofList
          [ "units", Json.String ^ unitsToString u
            "value", valueToJson v ]

  let pointValueToJson templateHandling fields = function
    | Event template ->
      match templateHandling with
      | ExpandTemplates ->
        let message = Logary.Formatting.MessageParts.formatTemplate template fields
        [ "message", Json.String message
          "template", Json.String template ]
      | IgnoreTemplates ->
        [ "message", Json.String template ]
    | Derived (v,u)
    | Gauge (v,u) ->
      [ "measure", fieldlikeToJson v (Some u) ]

  let fieldToJson (Field (v,uO)) = fieldlikeToJson v uO

  let messageToJson serviceName templateHandling (msg:Message) : Json =
    Json.Object ^ Map.ofList
      ( [ "level", Json.serialize msg.level
          "fields", Json.Object (msg.fields |> Map.toArray |> Array.map (fun (k,v) -> PointName.format k, fieldToJson v) |> Map.ofArray)
          "pointName", Json.serialize msg.name
          "context", Json.Object ^ Map.map (fun _ -> valueToJson) msg.context
          "name", Json.String ^ PointName.format msg.name
          "timestamp", Json.Number ^ decimal msg.timestamp
          "serviceName", Json.String ^ serviceName
        ] @ pointValueToJson templateHandling msg.fields msg.value
      )

  let serializeMessage serviceName templateHandling =
    messageToJson serviceName templateHandling
    >> Json.format

module Impl =
  type SumoState =
    { LastBatch : TargetMessage []
      Health : Health
    }
    static member empty = { LastBatch = Array.empty; Health = Live }
  and Health =
    | Live
    | Recovering of RecoveryState
  and RecoveryState =
    { Index : uint32
      FailedAttempts : uint32
      Recovered : uint32
    }

  let userAgent =
    UserAgent ^ sprintf "Logary.Targets.SumoLogic v%s"
#if INTERACTIVE
      "INTERACTIVE"
#else
      System.AssemblyVersionInformation.AssemblyInformationalVersion
#endif

  let requestAckJobCreator request =
    match request with
    | Log (_, ack) ->
      ack *<= ()

    | Flush (ackCh, nack) ->
      asJob (Ch.give ackCh () <|> nack)

  let extractMessage serviceName conf = function
    | Log (msg, _) -> Some ^ Serialization.serializeMessage serviceName conf.templateHandling msg
    | Flush _ -> None

  let sumoLogicLog = PointName [| "Logary"; "Targets"; "SumoLogic" |]

  let handleResponseBody (ri:RuntimeInfo) reqs (resp:Response) body =
    printfn "Received response: %i" resp.statusCode
    if resp.statusCode > 299 then
      ri.logger.errorWithBP (
        Message.eventX "Received HTTP {statusCode} response from SumoLogic; failing"
        >> Message.setField "statusCode" resp.statusCode
        >> Message.setField "body" body
        >> Message.setName sumoLogicLog )
      |> Job.start
      >>- fun () -> failwithf "got response code %i" resp.statusCode
    else
      ri.logger.verboseWithBP (
        Message.eventX "Successfully sent batch of {count} messages to SumoLogic"
        >> Message.setField "count" ^ Array.length reqs )
      |> Job.start
      >>= fun () -> Seq.iterJobIgnore requestAckJobCreator reqs

  let handleResponse (ri:RuntimeInfo) reqs =
    Job.useIn ^ fun resp ->
      Response.readBodyAsString resp
      >>= handleResponseBody ri reqs resp

  let buildBody serviceName conf =
    Array.choose ^ extractMessage serviceName conf
    >> String.concat "\n"
    >> BodyString

  let loop conf (ri: RuntimeInfo) (messages: RingBuffer<_>) (shutdown: Ch<_>) (saveWill:obj -> Job<unit>) (lastWill: obj option) =
    let baseRequest =
      Request.create Post conf.endpoint
      |> Request.keepAlive true
      |> Request.setHeader (RequestHeader.ContentType ^ ContentType.create ("application", "json"))
      |> Request.setHeader userAgent

    let buildRequest msgs =
      baseRequest |> Request.body ^ buildBody ri.serviceName conf msgs

    let sendBatch batch : Job<unit> =
      ri.logger.verboseWithBP (
        Message.eventX "SumoLogic target preparing to send batch of {count} messages to SumoLogic"
        >> Message.setField "count" ^ Array.length batch )
      |> Job.start
      >>- fun () -> buildRequest batch
      >>= (fun req -> timeOutMillis (int conf.timeoutMs) ^=>. Job.raises (exn "SumoLogic target timed out") <|> getResponse req)
      >>= handleResponse ri batch

    let saveWill : SumoState -> Job<unit> = box >> saveWill
    let lastWill : SumoState option = Option.map unbox lastWill

    let rec init () : Job<unit> =
      match lastWill with
      | Some state ->
        ri.logger.debugWithBP (Message.eventX "SumoLogic target failed; starting recovery")
        |> Job.start
        >>= fun () -> recover state
      | None ->
        ri.logger.verboseWithBP (Message.eventX "Starting SumoLogic target")
        |> Job.start
        >>= loop
    and recover ({ LastBatch = msgs; Health = health } as s) : Job<unit> =
      let nextRecoveryState =
        match health with
        | Live -> { Index = 0u; FailedAttempts = 0u; Recovered = 0u }
        | Recovering rs -> { rs with Index = rs.Index + 1u; FailedAttempts = rs.FailedAttempts }
      if nextRecoveryState.Index >= uint32 ^ Array.length msgs then
        ri.logger.debugWithBP (
          Message.eventX "SumoLogic target recovery complete; recovered {recovered} of {count} messages"
          >> Message.setField "attempts" nextRecoveryState.FailedAttempts
          >> Message.setField "recovered" nextRecoveryState.Recovered
          >> Message.setField "count" ^ Array.length msgs )
        |> Job.start
        >>= fun () -> saveWill ^ SumoState.empty
        >>= loop
      else if nextRecoveryState.FailedAttempts >= conf.maxRecoveryAttempts then
        ri.logger.debugWithBP (
          Message.eventX "SumoLogic target recovery failed after {attempts} attempts; recovered {recovered} of {count} messages"
          >> Message.setField "attempts" nextRecoveryState.FailedAttempts
          >> Message.setField "recovered" nextRecoveryState.Recovered
          >> Message.setField "count" ^ Array.length msgs )
        |> Job.start
        >>= fun () -> saveWill ^ SumoState.empty
        >>= loop
      else
        let nextStateIfSuccessful = { s with Health = Recovering { nextRecoveryState with Recovered = nextRecoveryState.Recovered + 1u } }
        ri.logger.debugWithBP (
          Message.eventX "SumoLogic target recovery in progress; retrying message {index} of {count}"
          >> Message.setField "index" (nextRecoveryState.Index + 1u)
          >> Message.setField "count" ^ Array.length msgs )
        |> Job.start
        >>= fun () -> saveWill ^ { s with Health = Recovering { nextRecoveryState with FailedAttempts = nextRecoveryState.FailedAttempts + 1u } }
        >>= fun () -> sendBatch [| msgs.[int nextRecoveryState.Index] |]
        >>= fun () -> saveWill ^ nextStateIfSuccessful
        >>= fun () -> recover nextStateIfSuccessful
    and loop () : Job<unit> =
      asJob ^ Alt.choose
        [ shutdown ^=> fun ack -> ack *<= ()

          RingBuffer.takeBatch (conf.batchSize) messages ^=> fun msgs ->
            saveWill ^ { SumoState.empty with LastBatch = msgs }
            >>= fun () -> sendBatch msgs
            >>= fun () -> saveWill ^ SumoState.empty
            >>= loop
        ]
    init ()

module SumoLogic =
  let create (conf : SumoLogicConf) : string -> TargetConf = TargetUtils.willAwareNamedTarget ^ Impl.loop conf

[<assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Logary.Targets.SumoLogic.Tests")>]
do ()
