package patches.buildTypes

import jetbrains.buildServer.configs.kotlin.v2019_2.*
import jetbrains.buildServer.configs.kotlin.v2019_2.BuildStep
import jetbrains.buildServer.configs.kotlin.v2019_2.buildSteps.powerShell
import jetbrains.buildServer.configs.kotlin.v2019_2.ui.*

/*
This patch script was generated by TeamCity on settings change in UI.
To apply the patch, change the buildType with id = 'Deploy'
accordingly, and delete the patch script.
*/
changeBuildType(RelativeId("Deploy")) {
    expectSteps {
        powerShell {
            name = "Setting octopus parameters"
            formatStderrAsError = true
            scriptMode = script {
                content = """
                    Write-Host "##teamcity[setParameter name='octopus.package' value='${'$'}(Get-ChildItem -Filter "*Installer.AdminApp*")']"
                                        Write-Host "##teamcity[setParameter name='octopus.packageVersion' value='${'$'}( Get-ChildItem -Filter "*Installer.AdminApp*" |% {  
                                                ${'$'}result = ${'$'}_.Name -match '(\d)\.(\d)\.(\d)(\-pre(\d+))?' 
                                                ${'$'}matches[0] })']"
                """.trimIndent()
            }
        }
        step {
            name = "Publish Package to Octopus"
            type = "octopus.push.package"
            executionMode = BuildStep.ExecutionMode.RUN_ON_SUCCESS
            param("octopus_forcepush", "true")
            param("octopus_host", "%octopus.server%")
            param("octopus_packagepaths", "%octopus.package%")
            param("secure:octopus_apikey", "%octopus.apiKey%")
        }
        step {
            name = "Create and Deploy Release"
            type = "octopus.create.release"
            executionMode = BuildStep.ExecutionMode.RUN_ON_SUCCESS
            param("octopus_additionalcommandlinearguments", """-v="AdminAppReleaseVersion:%adminApp.version%" -v="AdminAppVersion:%adminAppWeb.version%" --packageVersion=%octopus.packageVersion% --deploymenttimeout=%octopus.deployTimeout%""")
            param("octopus_channel_name", "%octopus.channel%")
            param("octopus_deployto", "%octopus.environment%")
            param("octopus_host", "%octopus.server%")
            param("octopus_project_name", "%octopus.project%")
            param("octopus_releasenumber", "%octopus.release%")
            param("octopus_version", "3.0+")
            param("secure:octopus_apikey", "%octopus.apiKey%")
        }
    }
    steps {
        update<BuildStep>(1) {
            param("secure:octopus_apikey", "credentialsJSON:a6d48763-5649-4d41-a824-8673ac64e7e3")
        }
        update<BuildStep>(2) {
            param("secure:octopus_apikey", "credentialsJSON:a6d48763-5649-4d41-a824-8673ac64e7e3")
        }
    }
}