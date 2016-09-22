# Logary.Targets.SumoLogic

A Logary target for the SumoLogic log management and data analytics service

[![NuGet](https://img.shields.io/nuget/v/Logary.Targets.SumoLogic.svg?maxAge=3600)](https://www.nuget.org/packages/Logary.Targets.SumoLogic)
[![Travis branch](https://img.shields.io/travis/neoeinstein/Logary.Targets.SumoLogic/master.svg?maxAge=3600)](https://travis-ci.org/neoeinstein/Logary.Targets.SumoLogic)


## Example usage:

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
