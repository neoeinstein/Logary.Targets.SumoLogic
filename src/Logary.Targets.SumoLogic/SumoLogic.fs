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

  let messageToJson templateHandling (msg:Message) : Json =
    Json.Object ^ Map.ofList
      ( [ "level", Json.serialize msg.level
          "fields", Json.Object (msg.fields |> Map.toArray |> Array.map (fun (k,v) -> PointName.format k, fieldToJson v) |> Map.ofArray)
          "pointName", Json.serialize msg.name
          "context", Json.Object ^ Map.map (fun _ -> valueToJson) msg.context
          "name", Json.String ^ PointName.format msg.name
          "timestamp", Json.Number ^ decimal msg.timestamp
        ] @ pointValueToJson templateHandling msg.fields msg.value
      )

  let serializeMessage templateHandling =
    messageToJson templateHandling
    >> Json.format

module Impl =
  let userAgent =
    UserAgent ^ sprintf "Logary.Targets.SumoLogic v%s"
#if INTERACTIVE
      "INTERACTIVE"
#else
      System.AssemblyVersionInformation.InformationalVersion
#endif

  let requestAckJobCreator request =
    match request with
    | Log (_, ack) ->
      ack *<= ()

    | Flush (ackCh, nack) ->
      asJob (Ch.give ackCh () <|> nack)

  let extractMessage conf = function
    | Log (msg, _) -> Some ^ Serialization.serializeMessage conf.templateHandling msg
    | Flush _ -> None

  let sumoLogicLog = PointName [| "Logary"; "Targets"; "SumoLogic" |]

  let handleResponseBody (ri:RuntimeInfo) reqs (resp:Response) body =
    printfn "Received response: %i" resp.statusCode
    if resp.statusCode > 299 then
      Message.eventError "Received HTTP {statusCode} response from SumoLogic; failing"
      |> Message.setField "statusCode" resp.statusCode
      |> Message.setField "body" body
      |> Message.setName sumoLogicLog
      |> Logger.log ri.logger
      >>- fun () -> failwithf "got response code %i" resp.statusCode
    else
      Message.eventVerbose "Successfully sent batch of {count} messages to SumoLogic"
      |> Message.setField "count" ^ Array.length reqs
      |> Logger.log ri.logger
      >>=. Seq.iterJobIgnore requestAckJobCreator reqs

  let handleResponse (ri:RuntimeInfo) reqs =
    Job.useIn ^ fun resp ->
      Response.readBodyAsString resp
      >>= handleResponseBody ri reqs resp

  let messagesReceivedPointName = PointName [| "Logary"; "Targets"; "SumoLogic"; "messagesReceived" |]

  let loop conf (ri: RuntimeInfo) (requests: RingBuffer<_>) (shutdown: Ch<_>) =
    let baseRequest =
      Request.create Post conf.endpoint
      |> Request.keepAlive true
      |> Request.setHeader (RequestHeader.ContentType ^ ContentType.create ("application", "json"))
      |> Request.setHeader userAgent

    let rec loop () : Job<unit> =
      asJob ^ Alt.choose
        [ shutdown ^=> fun ack -> ack *<= ()

          RingBuffer.takeBatch (uint32 conf.batchSize) requests ^=> fun reqs ->
            Message.gauge messagesReceivedPointName (Value.Int64 ^ int64 ^ Array.length reqs)
            |> ri.logger.logSimple

            let body =
              reqs
              |> Seq.choose ^ extractMessage conf
              |> String.concat "\n"

            let req =
              baseRequest
              |> Request.body (BodyString body)

            getResponse req
            >>= handleResponse ri reqs
            >>= loop
        ]
    loop ()

module SumoLogic =
  let create (conf : SumoLogicConf) : string -> TargetConf = TargetUtils.stdNamedTarget ^ Impl.loop conf

[<assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Logary.Targets.SumoLogic.Tests")>]
do ()
