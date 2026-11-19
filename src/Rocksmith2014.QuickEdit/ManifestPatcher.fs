module Rocksmith2014.QuickEdit.ManifestPatcher

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks
open Rocksmith2014.PSARC

let private dvdRegex = Regex("\"DynamicVisualDensity\"\\s*:\\s*\\[(?<arr>[^\\]]*)\\]", RegexOptions.Compiled)
let private mpdRegex = Regex("\"MaxPhraseDifficulty\"\\s*:\\s*(?<n>\\d+)", RegexOptions.Compiled)
let private numRegex = Regex("-?\\d+(\\.\\d+)?", RegexOptions.Compiled)

let private inv = CultureInfo.InvariantCulture

/// Same curve as the one used when building packages: geometric from the begin speed down to the end speed.
let private curve (beginSpeed: float) (endSpeed: float) (levels: int) (count: int) =
    let maxLevel = (min levels 20) - 1
    let factor = if maxLevel > 0 then Math.Pow(endSpeed / beginSpeed, 1. / float maxLevel) else 1.

    Array.init count (fun i ->
        if i >= maxLevel then endSpeed else Math.Round(beginSpeed * Math.Pow(factor, float i), 1))

/// Rewrites the DynamicVisualDensity arrays of instrumental arrangements in the manifest text. Returns the new text and the number of changed arrays.
let patchText (speed: float) (text: string) =
    let sb = StringBuilder(text.Length)
    let mutable last = 0
    let mutable changed = 0

    for m in dvdRegex.Matches text do
        let arr = m.Groups["arr"]
        let numbers = numRegex.Matches arr.Value
        let mpd = mpdRegex.Match(text, m.Index + m.Length)

        if numbers.Count > 0 && mpd.Success then
            let beginSpeed = Double.Parse(numbers[0].Value, inv)

            let endSpeed = Double.Parse(numbers[numbers.Count - 1].Value, inv)

            // Vocals use a flat 2.0 array
            if beginSpeed > endSpeed && beginSpeed > speed then
                let values = curve beginSpeed speed (Int32.Parse(mpd.Groups["n"].Value) + 1) numbers.Count
                let mutable i = 0

                let newArr =
                    numRegex.Replace(arr.Value, fun _ ->
                        let v = values[i].ToString("0.0", inv)
                        i <- i + 1
                        v)

                sb.Append(text, last, arr.Index - last).Append(newArr) |> ignore
                last <- arr.Index + arr.Length
                changed <- changed + 1

    sb.Append(text, last, text.Length - last) |> ignore
    sb.ToString(), changed

/// Patches the scroll speed in all manifests of the PSARC, in place. Returns the number of changed arrangements.
[<CompiledName("PatchAsync")>]
let patchAsync (psarcPath: string) (speed: float) : Task<int> =
    task {
        use psarc = PSARC.OpenFile psarcPath
        let mutable total = 0

        let edit (entries: NamedEntry list) =
            entries
            |> List.map (fun e ->
                if e.Name.StartsWith("manifests/", StringComparison.Ordinal)
                   && (e.Name.EndsWith(".json", StringComparison.Ordinal) || e.Name.EndsWith(".hsan", StringComparison.Ordinal)) then
                    e.Data.Position <- 0L
                    let text = (new StreamReader(e.Data, UTF8Encoding(false), false, 4096, true)).ReadToEnd()
                    let patched, n = patchText speed text

                    if n > 0 then
                        total <- total + n
                        e.Data.SetLength 0L
                        e.Data.Position <- 0L
                        let bytes = UTF8Encoding(false).GetBytes patched
                        e.Data.Write(bytes, 0, bytes.Length)
                        e.Data.Position <- 0L

                    e
                else
                    e)

        do! psarc.Edit({ Mode = TempFiles; EncryptTOC = true }, edit)
        return total
    }
