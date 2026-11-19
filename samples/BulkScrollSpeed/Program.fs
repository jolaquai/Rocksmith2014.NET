open System
open System.IO
open Rocksmith2014.QuickEdit

type Options =
    { Paths: string list
      Speed: float
      OutDir: string option
      InPlace: bool
      Manifests: bool }

let usage = "Usage: BulkScrollSpeed <psarc|directory>... [--speed 0.9] [--manifests] (--out <dir> | --in-place)"

let parseArgs (argv: string list) =
    let rec loop opts = function
        | [] -> Some opts
        | "--speed" :: v :: rest -> loop { opts with Speed = Double.Parse(v, Globalization.CultureInfo.InvariantCulture) } rest
        | "--out" :: v :: rest -> loop { opts with OutDir = Some v } rest
        | "--in-place" :: rest -> loop { opts with InPlace = true } rest
        | "--manifests" :: rest -> loop { opts with Manifests = true } rest
        | flag :: _ when flag.StartsWith "--" -> None
        | path :: rest -> loop { opts with Paths = opts.Paths @ [ path ] } rest

    loop { Paths = []; Speed = 0.9; OutDir = None; InPlace = false; Manifests = false } argv
    |> Option.filter (fun o -> not o.Paths.IsEmpty && o.InPlace <> o.OutDir.IsSome)

let copyFile (source: string) (target: string) =
    task {
        use s = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous ||| FileOptions.SequentialScan)
        use d = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous ||| FileOptions.SequentialScan)
        do! s.CopyToAsync d
    }

let findPsarcs (paths: string list) =
    paths
    |> List.collect (fun p ->
        if Directory.Exists p then
            Directory.EnumerateFiles(p, "*.psarc", SearchOption.AllDirectories) |> Seq.toList
        else
            [ p ])

[<EntryPoint>]
let main argv =
    match parseArgs (List.ofArray argv) with
    | None ->
        Console.Error.WriteLine usage
        2
    | Some opts ->
        let mutable failures = 0

        for psarc in findPsarcs opts.Paths do
            let name = Path.GetFileName psarc

            try
                let target =
                    match opts.OutDir with
                    | Some dir ->
                        Directory.CreateDirectory dir |> ignore
                        Path.Combine(dir, name)
                    | None ->
                        psarc

                let tmp = target + ".tmp"
                let changed =
                    if opts.Manifests then
                        (task {
                            do! copyFile psarc tmp
                            return! ManifestPatcher.patchAsync tmp opts.Speed
                        }).GetAwaiter().GetResult()
                    else
                        (ScrollSpeedEditor.applyAsync psarc tmp opts.Speed).GetAwaiter().GetResult()

                if changed = 0 then
                    File.Delete tmp
                    printfn "SKIP  %s" name
                else
                    if opts.InPlace then
                        File.Replace(tmp, psarc, psarc + ".bak")
                    else
                        File.Move(tmp, target, overwrite = true)

                    printfn "OK    %s (%d arrangement(s))" name changed
            with e ->
                failures <- failures + 1
                eprintfn "FAIL  %s: %s" name e.Message

        if failures > 0 then 1 else 0
