module Rocksmith2014.QuickEdit.ScrollSpeedEditor

open System
open System.IO
open System.Threading.Tasks
open Rocksmith2014.Common
open Rocksmith2014.DD
open Rocksmith2014.DLCProject
open Rocksmith2014.DLCProject.PsarcImportTypes

/// Bass is already an octave down in Rocksmith; A220 on it is always wrong (the A220 trick belongs on guitar parts).
let private hasBogusBassPitch (inst: Instrumental) =
    inst.Name = ArrangementName.Bass && inst.TuningPitch < 230.

/// Sets the scroll speed of all instrumental arrangements, generates DD for those that have none and fixes bass parts charted at A220.
/// The result is written to the target path, which may be the source path.
/// Returns the number of changed arrangements; nothing is written if it is zero.
[<CompiledName("ApplyAsync")>]
let applyAsync (psarcPath: string) (targetPath: string) (speed: float) : Task<int> =
    task {
        let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        Directory.CreateDirectory tempDir |> ignore

        try
            let! r = PsarcImporter.import ignore psarcPath tempDir
            let project = r.GeneratedProject

            let idsWithoutDD =
                r.ArrangementData
                |> List.choose (function
                    | arr, ImportedData.Instrumental xml when xml.Levels.Count = 1 -> Some(Arrangement.getId arr)
                    | _ -> None)
                |> Set.ofList

            let mutable changed = 0

            let arrangements =
                project.Arrangements
                |> List.map (function
                    | Instrumental inst when inst.ScrollSpeed <> speed || idsWithoutDD.Contains inst.Id || hasBogusBassPitch inst ->
                        changed <- changed + 1
                        let inst = if hasBogusBassPitch inst then { inst with TuningPitch = 440. } else inst
                        Instrumental { inst with ScrollSpeed = speed }
                    | other -> other)

            if changed > 0 then
                let config: PackageBuilder.BuildConfig =
                    { Platforms = [ Platform.fromPackageFileName psarcPath ]
                      BuilderVersion = "Rocksmith2014.NET QuickEdit"
                      Author = project.Author |> Option.defaultValue ""
                      AppId = r.AppId |> Option.defaultValue AppId.CherubRock
                      GenerateDD = true
                      ForcePhraseCreation = false
                      DDConfig = { PhraseSearchThreshold = Some 80; LevelCountGeneration = LevelCountGeneration.Simple }
                      ApplyImprovements = false
                      SaveDebugFiles = false
                      AudioConversionTask = async.Zero()
                      IdResetConfig = None
                      ProgressReporter = None }

                try
                    let! _ =
                        PackageBuilder.buildPackages
                            (PackageBuilder.WithPlatformAndExtension targetPath)
                            config
                            { project with Arrangements = arrangements }

                    ()
                with e ->
                    let listing =
                        if Directory.Exists tempDir then
                            Directory.GetFiles tempDir
                            |> Array.map (fun f -> $"{Path.GetFileName f} ({FileInfo(f).Length} B)")
                            |> String.concat ", "
                        else
                            "<temp directory missing>"

                    raise (Exception($"Build failed. AlbumArtFile=%s{project.AlbumArtFile} exists=%b{File.Exists project.AlbumArtFile}; temp dir: %s{listing}", e))

            return changed
        finally
            Directory.Delete(tempDir, recursive = true)
    }
