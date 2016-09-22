namespace Logary.Targets

open Logary.Target

type TemplateHandlingConf =
  | ExpandTemplates
  | IgnoreTemplates

type SumoLogicConf =
  {
    endpoint : System.Uri
    templateHandling : TemplateHandlingConf
    batchSize : uint16
    maxRecoveryAttempts : uint32
  }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SumoLogicConf =
  val empty : SumoLogicConf
  val create : endpoint:System.Uri -> templateHandling:TemplateHandlingConf -> SumoLogicConf

module SumoLogic =
  val create : conf:SumoLogicConf -> (string -> TargetConf)
