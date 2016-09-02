module Logary.Targets.SumoLogic.Tests

open Logary.Targets
open Fuchu
open Swensen.Unquote

[<Tests>]
let tests =
  testList "SumoLogic configuration" [
    testProperty "Can create configuration" <| fun templ ->
      let actual = SumoLogicConf.create (System.Uri "http://localhost") templ
      actual.templateHandling =! templ
    testCase "Empty is not null" <| fun () ->
      SumoLogicConf.empty <>! Unchecked.defaultof<_>
  ]
