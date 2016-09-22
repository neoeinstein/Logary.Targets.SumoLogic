namespace Logary.Targets

open Logary.Target

/// Determines how the target will handle message templates
type TemplateHandlingConf =
  /// Attempts to expand all messages as templates. The raw message is encoded
  /// as the `template` while the expanded message is encoded as `message`
  | ExpandTemplates
  /// Does not expand any message templates and encodes the raw message as
  /// `message`
  | IgnoreTemplates

/// Configuration for a SumoLogic Logary target
type SumoLogicConf =
  {
    /// The SumoLogic HTTP Source endpoint. For more information
    /// see the [SumoLogic documentation](https://help.sumologic.com/Send_Data/Sources/HTTP_Source)
    endpoint : System.Uri
    /// Determines how the target will handle message templates
    templateHandling : TemplateHandlingConf
    /// The maximum number of messages to process at one time
    batchSize : uint16
    /// The maximum number of failures to be tolerated before dropping
    /// all messages in a batch and proceeding to accept a new batch
    maxRecoveryAttempts : uint32
  }

/// Utilities for configuring a SumoLogic Logary target
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SumoLogicConf =
  /// A default SumoLogic configuration which posts messages to localhost,
  /// ignores templates, uses a batch size of 100, and will attempt to recover
  /// a batch at most 3 times
  val empty : SumoLogicConf
  /// Initializes a default SumoLogic configuration with the default batch size (100)
  /// and that will attempt to recover a batch at most 3 times
  val create : endpoint:System.Uri -> templateHandling:TemplateHandlingConf -> SumoLogicConf

/// Logary target for SumoLogic
module SumoLogic =
  /// Creates a new target for SumoLogic using the provided configuration
  val create : conf:SumoLogicConf -> (string -> TargetConf)
