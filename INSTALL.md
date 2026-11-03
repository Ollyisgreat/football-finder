# Installing Xtream Football Finder (from your phone)

## 1. Build the plugin (free, about 5 minutes, all in the phone browser)
1. Sign in at github.com (a free account is fine) → **+ → New repository** → name it `football-finder` → **Private** → Create.
2. Tap **Add file → Create new file**. For the file name type exactly: `.github/workflows/build-plugin.yml`
3. Open `build-plugin.yml`, select all, copy, paste it into the editor → **Commit changes**.
4. Open the **Actions** tab. If GitHub asks, tap the button to enable workflows. Wait for the green tick (2–4 min).
5. Back on the repo page → **Releases** → open the latest build → download the zip that matches your Jellyfin version
   (Dashboard → the version number is shown at the bottom / under "Server"):
   - `XtreamFootball-jellyfin-12.zip` → Jellyfin 12.x
   - `XtreamFootball-jellyfin-10.11.zip` → Jellyfin 10.11.x
   - `XtreamFootball-jellyfin-10.10.zip` → Jellyfin 10.10.x
6. Unzip it (iPhone: tap it in the Files app. Android: the Files app or Material Files). You get a folder called `XtreamFootball_1.0.0.0`.

## 2. Upload with FTP/SFTP
Upload the **whole folder** `XtreamFootball_1.0.0.0` into Jellyfin's `plugins` folder. You should end up with:

```
plugins/
  XtreamFootball_1.0.0.0/
    XtreamFootball.dll
    meta.json
```

Where `plugins` usually is:
| Setup | Plugins folder |
|---|---|
| Linux (apt / deb install) | `/var/lib/jellyfin/plugins` |
| Docker, official image | `<your config folder>/plugins` |
| Docker, linuxserver.io image | `<your config folder>/data/plugins` |
| Windows | `C:\ProgramData\Jellyfin\Server\plugins` |

Tip: it's the folder that already holds a `configurations` folder and any other plugins.

## 3. Restart Jellyfin
Dashboard → **Restart** (on Docker you may need to restart the container).
Then Dashboard → **Plugins** → you should see **Xtream Football Finder** as Active.

## 4. Use it
Dashboard → Plugins → **Xtream Football Finder** (also in the dashboard menu as "XtreamFootball"):
1. Enter server URL, username, password → **Save & test login**.
2. Add your teams (one per line) → **Save**.
3. Type a team → **Search**. The first search downloads the whole TV guide, which can take a minute or two.
   Every channel found has **Copy link** (paste it into VLC or an IPTV app) and **Open**.

Optional: to watch inside Jellyfin, copy the two links from "Watch inside Jellyfin" into
Dashboard → Live TV → Tuner Devices (M3U Tuner) and TV Guide Data Providers (XMLTV).

## If something goes wrong
- Plugin shows "Not supported" / "Malfunctioned": you probably uploaded the zip for a different Jellyfin version.
- Not listed at all: check the folder sits directly inside `plugins` (not `plugins/XtreamFootball_1.0.0.0/XtreamFootball_1.0.0.0`).
- Linux: the files must be readable by the `jellyfin` user (permissions 644 for files and 755 for the folder are fine).
- Logs: Dashboard → Logs, search for "XtreamFootball".
- A red ✗ in GitHub Actions: open the failed run and copy the error text into the chat.
