# When arctium.io is down

The Cataclysm Classic 4.4.2 client can't start without a version server that still knows its build, and the Arctium Game Launcher uses Arctium's, at `ngdp.arctium.io`. While that server is down, the client closes at startup. This page shows how to stand in for it on your own PC, which takes a few minutes and keeps working whether or not Arctium comes back.

## Symptoms

The client quits before the login screen, or shows "CASC system unable to initialize". The newest report in the game's `Errors` folder begins with:

```
ERROR #134 (0x85100086) Fatal condition!
Description: Failed to download required build info asset
```

and the Arctium Game Launcher prints `http://ngdp.arctium.io/EU/wow/60895/versions not reachable. Falling back to:` followed by Blizzard's addresses.

## Cause

On every start the client asks a version server which build to load and which configuration files describe it. Blizzard's version server lists only the newest Classic build (5.5.4 since October 2026), and Blizzard's download servers no longer carry 4.4.2's configuration files, so the launcher points the client at Arctium's version server instead. When that server is unreachable, the launcher falls back to Blizzard's and the client tries to load a build it isn't.

The proxy plays no part: this happens before the client connects to it.

## Workaround: answer the client yourself

Your installation keeps 4.4.2's configuration files in `Data\config`, and a version file naming them is all the client needs.

1. Open PowerShell in the game's top folder (the one that holds `Data`) and run:
   ```powershell
   $out = "$env:LOCALAPPDATA\HermesProxy\ngdp-4.4.2"
   New-Item -ItemType Directory -Force $out | Out-Null
   $configs = Get-ChildItem Data\config -Recurse -File
   $build = $configs | Where-Object { Select-String -LiteralPath $_.FullName -SimpleMatch 'build-name = WOW-60895patch4.4.2' -Quiet } | Select-Object -First 1
   if (-not $build) { throw 'No 4.4.2.60895 build configuration in Data\config' }
   # the CDN configuration the client wrote together with it
   $cdn = $configs | Where-Object { (Get-Content -LiteralPath $_.FullName -TotalCount 1) -eq '# CDN Configuration' -and
       [Math]::Abs(($_.LastWriteTime - $build.LastWriteTime).TotalMinutes) -lt 5 } | Select-Object -First 1
   if (-not $cdn) { throw 'No CDN configuration next to it in Data\config' }
   "build configuration: $($build.Name)"
   "CDN configuration:   $($cdn.Name)"
   $row = "|$($build.Name)|$($cdn.Name)||60895|4.4.2.60895|f337a1b6ece3dacf3ec807287f3c2aa6"
   $lines = @('Region!STRING:0|BuildConfig!HEX:16|CDNConfig!HEX:16|KeyRing!HEX:16|BuildId!DEC:4|VersionsName!String:0|ProductConfig!HEX:16', '## seqn = 1') +
       @('us', 'eu', 'kr', 'tw', 'cn' | ForEach-Object { $_ + $row })
   [IO.File]::WriteAllText("$out\versions", ($lines -join "`n") + "`n")
   Invoke-WebRequest http://eu.patch.battle.net:1119/wow_classic/cdns -OutFile "$out\cdns" -UseBasicParsing
   ```
   It writes two files, `versions` and `cdns`, and prints the two configuration files it chose. This is needed once per installation.
2. Serve that folder while you play, for example with Python, from the same PowerShell window:
   ```powershell
   python -m http.server 8099 --bind 127.0.0.1 --directory "$env:LOCALAPPDATA\HermesProxy\ngdp-4.4.2"
   ```
3. Start the launcher with two more options, both of them:
   ```
   --versionurl http://127.0.0.1:8099/versions --cdnsurl "http://127.0.0.1:8099/cdns?"
   ```
   Keep the `?`. The launcher writes the address over the client's built-in one without ending it, and the `?` turns what is left of the old address into a query string the file server ignores.

The launcher then lists `http://127.0.0.1:8099/versions` as its version source, and the client starts as before. Game data still comes from your installation; only the version answer is local, so this keeps working whether or not Arctium's server is up.

## What the files say

`versions` holds one line per region:

```
eu|<build configuration>|<CDN configuration>||60895|4.4.2.60895|f337a1b6ece3dacf3ec807287f3c2aa6
```

The last field is the Classic product configuration recorded for 4.4.2.60895 on [wago.tools](https://wago.tools/builds); without it the client stops with "CASC system unable to initialize". The CDN configuration differs between installations, which is why the script reads it from yours rather than naming one. `cdns` is Blizzard's own list of download servers, unchanged.
