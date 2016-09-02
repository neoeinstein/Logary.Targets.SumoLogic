(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"

#r "Hopac.Core"
#r "Hopac"
#r "Hopac.Platform"

(**
Logary.Targets.SumoLogic
========================

A [Logary][] target for the [SumoLogic][] log management and data analytics service

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The Logary.Targets.SumoLogic library can be <a href="https://nuget.org/packages/Logary.Targets.SumoLogic">installed from NuGet</a>:
      <pre>PM> Install-Package Logary.Targets.SumoLogic</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Example
-------

This example demonstrates configuring the target with [Logary][].

*)
#r "Logary"
#r "Logary.Targets.SumoLogic"
open Hopac
open Logary
open Logary.Configuration
open Logary.Targets

let sumoEndpoint =
  System.Uri "https://endpoint1.collection.us2.sumologic.com/receiver/v1/http/HTTP_COLLECTOR_ENDPOINT"

let li =
  Promise.start
    ( withLogary "Example for using SumoLogic target"
      ( withTargets [
          SumoLogic.create (SumoLogicConf.create sumoEndpoint ExpandTemplates) "sumologic"
        ] >>
        withRules [
          Rule.createForTarget "sumologic"
        ]
      )
    )


(**
Check out the [Logary][] docs for more information.

Samples & documentation
-----------------------

The library comes with comprehensible documentation automatically generated from `*.fsx` files in [the content folder][content].
The API reference is automatically generated from Markdown comments in the library implementation.

 * [API Reference](reference/index.html) contains automatically generated documentation for all types, modules
   and functions in the library. This includes additional brief samples on using most of the
   functions.

Contributing and copyright
--------------------------

The project is hosted on [GitHub][gh] where you can [report issues][issues], fork
the project and submit pull requests. If you're adding a new public API, please also
consider adding [samples][content] that can be turned into a documentation. You might
also want to read the [library design notes][readme] to understand how it works.

The library is available under the Apache 2.0 license, which allows modification and
redistribution for both commercial and non-commercial purposes. For more information see the
[License file][license] in the GitHub repository.

  [Logary]: https://logary.github.io/
  [SumoLogic]: https://www.sumologic.com/
  [content]: https://github.com/fsprojects/Logary.Targets.SumoLogic/tree/master/docs/content
  [gh]: https://github.com/fsprojects/Logary.Targets.SumoLogic
  [issues]: https://github.com/fsprojects/Logary.Targets.SumoLogic/issues
  [readme]: https://github.com/fsprojects/Logary.Targets.SumoLogic/blob/master/README.md
  [license]: https://github.com/fsprojects/Logary.Targets.SumoLogic/blob/master/LICENSE.txt
*)
