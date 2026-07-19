# Fleet deployment (`deploy/`) — VPS `bbs-host-1`

This folder is the source of truth for what runs on the hosted-worlds VPS
(31.70.113.90, Debian 13). `deploy.yml` (GitHub Actions, manual dispatch, `production`
environment with an approval gate) rsyncs these folders to `/opt/bbs/` and runs
`remote-deploy.sh` over SSH. Architecture: `docs/developer/HOSTED_WORLDS.md`; the
bug-report inbox: `docs/developer/REPORT_HOST.md`.

| Folder | Service | Image | Cadence |
|---|---|---|---|
| `caddy/` | caddy-docker-proxy (TLS + routing) | `lucaslorentz/caddy-docker-proxy` | rarely (config change) |
| `worldhost/` | hosted-worlds control plane | `ghcr.io/marceld23/blocks-beyond-the-stars-worldhost` (`worldhost-image.yml`) | on service change |
| `reports/` | bug-report inbox | `ghcr.io/marceld23/blocks-beyond-the-stars-reports` (`reports-image.yml`) | on service change |
| `ai/` | LLM texts (NPC lines/missions) | `ghcr.io/marceld23/blocks-beyond-the-stars-ai` (`ai-image.yml`) | on service change |

Per-world game containers are NOT deployed from here — WorldHost starts them on demand from the
dedicated-server image pinned in `/opt/bbs/worldhost/.env` (`BBS_WH_SERVER_IMAGE`; that image is
built by `docker.yml` on release tags). Each world container runs with hard resource fences
(`BBS_WH_INSTANCE_MEMORY`/`_CPUS`, pids cap) and the fleet keeps at most `BBS_WH_MAX_ACTIVE`
instances awake — overload degrades to a friendly "no capacity" error, never an OOM'd host.

The `ai/` service is **internal-only**: no published port, no Caddy labels — world containers reach
it as `http://ai:8077` on the shared network, and the LLM provider's API key never leaves
`/opt/bbs/ai/.env`. The operator admin UI lives at `https://<portal>/admin`
(`BBS_WH_ADMIN_USER`/`_PASSWORD`) and at `https://<reports>/admin` for the bug-report inbox.

## Secrets model

- GitHub holds exactly **one** deploy secret: `DEPLOY_SSH_KEY` (environment `production`), a
  dedicated ed25519 key for the `bbs` user. The VPS host key is pinned in `deploy.yml`.
- **All service secrets live only on the host** in `/opt/bbs/<service>/.env` (mode 600, owner
  `bbs`) — created once from the `.env.example` files here. CI never reads or writes them;
  `remote-deploy.sh` rewrites only the `*_TAG` line when a deploy pins a new image version.

## Version pinning & rollback

The image workflows publish `:latest` plus an immutable `:sha-<short>` on every main push that
touches the service. Deploys should pin `sha-<short>` (dispatch input); rollback = rerun the deploy
with the previous sha. Rolling the game-server fleet = edit `BBS_WH_SERVER_IMAGE` in
`/opt/bbs/worldhost/.env` and redeploy `worldhost` — running worlds keep their image until their
idle shutdown, new wakes use the new pin.

## Browser client at /play (WebGL build)

The portal serves the Unity WebGL browser client at `https://<portal>/play` — the My-Worlds Play
button deep-links into it with the world's wss URL + join token, so browser players land in their
world with one click. The build is injected out-of-band (the worldhost image cannot build Unity):

```sh
# on the VPS, once per client release that ships a webgl*.zip asset (run as root — the
# extracted files are root-owned). Clear the folder IN PLACE: it is bind-mounted read-only
# into the running container, so `rm -rf webgl && mkdir webgl` would replace the directory
# inode and silently break the mount until the next container recreate.
cd /opt/bbs/worldhost
find webgl -mindepth 1 -delete
curl -fL -o /tmp/webgl.zip "https://github.com/marceld23/BlocksBeyondTheStars/releases/download/v<version>/BlocksBeyondTheStars-webgl-<version>.zip"
unzip -q /tmp/webgl.zip -d webgl && rm /tmp/webgl.zip
cat webgl/version.txt   # must print <version>
```

The folder is bind-mounted read-only at `/app/webgl` (`BBS_WH_WEBGL_DIR`). Empty folder = a friendly
"not installed" page. The deep-link needs a build that understands the `hosted_token`/`world_id`
query parameters (v0.8.0+).

## Per-release runbook — rolling the fleet to a new game version

Proven on v0.8.4/v0.8.5. Precondition: `release.yml` is green for tag `vX.Y.Z` (it publishes the
dedicated-server image `ghcr.io/marceld23/blocks-beyond-the-stars-server:X.Y.Z` and the
`BlocksBeyondTheStars-webgl-X.Y.Z.zip` release asset).

1. **Pre-pull the game-server image** on the host, so the first world wake doesn't pay the pull:
   `docker pull ghcr.io/marceld23/blocks-beyond-the-stars-server:X.Y.Z`
2. **Update the fleet pin** (this is *not* covered by `deploy.yml`, which only rewrites the
   control-plane `*_TAG` lines): back up and edit `/opt/bbs/worldhost/.env` →
   `BBS_WH_SERVER_IMAGE=ghcr.io/marceld23/blocks-beyond-the-stars-server:X.Y.Z`
   (`cp .env .env.bak.pre-XYZ` first — that backup is also the rollback path).
3. **Redeploy `worldhost`** via the Deploy (VPS) workflow (service=`worldhost`; set
   `worldhost_tag=sha-<short>` when the control plane itself changed since the last deploy —
   check `worldhost-image.yml` runs). The `production` environment gate must be approved.
4. **Recycle the keep-awake arcade pool worlds.** They never idle-exit
   (`BBS_WH_GLITCH_KEEP_AWAKE`), so unlike normal worlds they would keep the old image forever:
   `docker rm -f bbs-world-<id>` per pool world — WorldHost re-wakes each on the new pin within
   ~20 s via the crash→re-wake path. Verify the image with `docker ps`.
   Normal hosted worlds need nothing: they pick up the new pin on their next wake.
5. **Refresh `/play`** with the release's `webgl` zip — see the in-place snippet above.
   Verify: portal `/play` answers 200 and `webgl/version.txt` prints `X.Y.Z`.
6. **Config check:** scan the release notes for new env knobs. Defaults are chosen to work for
   this fleet unless the notes say otherwise — only edit `/opt/bbs/*/.env` when a knob's default
   does not fit.

Rollback = restore the `.env` backup, redeploy `worldhost`, recycle the pool worlds again.

## One-time host prerequisites (already done on bbs-host-1, 2026-07-04)

Deploy user `bbs` (docker group), ufw (22, 80, 443/tcp+udp, 32000-32999/udp), Docker Engine +
compose plugin, shared network `docker network create bbs-hosted`, `/opt/bbs/{caddy,worldhost,reports}`
with the real `.env` files, and the deploy public key in `~bbs/.ssh/authorized_keys`
(`no-agent-forwarding,no-port-forwarding,no-X11-forwarding`). DNS (Strato, manual): A `play`,
wildcard A `*.play` and A `reports` → the VPS.
