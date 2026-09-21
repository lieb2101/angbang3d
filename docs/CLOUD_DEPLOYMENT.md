# Angband3D — Cloud Deployment & Container Guide

## Architecture Overview (Option A)

Angband3D implements a **decoupled client-server architecture**:
- **Authoritative Engine Service (`server/`)**: Runs a headless, single-binary C Angband 4.2.6 engine compiled with the JSON Bridge protocol (`-mbridge`).
- **WebSocket Gateway (`/ws`)**: A lightweight Node.js daemon manages client connections, spawns isolated child engine processes per session, and pipes stdin/stdout streams to/from WebSocket frames in real-time.
- **Save Game Storage (`/data/save`)**: Persistent volume or cloud storage mount preserving binary character save files (`SaveVNLA` format).
- **Static Asset Delivery**: Serves compiled Godot Web client files (`index.html`, `index.wasm`, `index.pck`) as well as the pre-built standalone desktop distribution (`/download/angband3d-standalone.zip`).

---

## Container Deployment

### 1. Docker Build

Build the multi-stage Linux container:
```bash
docker build -t angband3d-cloud:latest -f server/Dockerfile .
```

### 2. Docker Compose (Local or VPS)

Run with persistent character saves:
```bash
cd server
docker compose up -d
```

Verify service health:
```bash
curl http://localhost:8080/health
```

Expected response:
```json
{
  "status": "ok",
  "uptime": 12.4,
  "engine": true,
  "version": "1.0.0"
}
```

---

## Cloud Hosting Platforms

### Google Cloud Run

Google Cloud Run provides fully managed serverless containers with WebSocket support and scale-to-zero.

1. **Enable Session Affinity & WebSockets**:
   In Cloud Run service settings, set:
   - **Session Affinity**: Enabled
   - **Request Timeout**: 3600 seconds (1 hour session duration)
   - **Concurrency**: 80 connections per container instance
   - **Memory**: 512MiB - 1GiB
   - **CPU**: 1 vCPU

2. **Persistent Storage**:
   Mount a Google Cloud Storage (GCS) bucket as a persistent volume using Cloud Run Volume Mounts at `/data/save` so player saves survive container restarts.

3. **Deploy via gcloud**:
   ```bash
   gcloud run deploy angband3d-cloud \
     --image gcr.io/PROJECT_ID/angband3d-cloud:latest \
     --platform managed \
     --region us-central1 \
     --allow-unauthenticated \
     --port 8080 \
     --session-affinity \
     --timeout 3600
   ```

### Fly.io

Fly.io provides global edge containers with persistent NVMe volumes.

1. Create `fly.toml`:
   ```toml
   app = "angband3d-cloud"
   primary_region = "ord"

   [build]
     dockerfile = "server/Dockerfile"

   [http_service]
     internal_port = 8080
     force_https = true
     auto_stop_machines = false
     auto_start_machines = true
     min_machines_running = 1

   [mounts]
     source = "angband_saves"
     destination = "/data/save"
   ```

2. Allocate persistent volume and deploy:
   ```bash
   fly volumes create angband_saves --size 1
   fly deploy
   ```

---

## Reverse Proxy & TLS Configuration (Nginx / Caddy)

If hosting on a Linux VPS behind Nginx, configure WebSocket proxying:

```nginx
server {
    server_name angband.example.com;
    listen 443 ssl http2;

    # SSL certificates (Let's Encrypt / Certbot)
    ssl_certificate /etc/letsencrypt/live/angband.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/angband.example.com/privkey.pem;

    # WebSocket Relay
    location /ws {
        proxy_pass http://127.0.0.1:8080/ws;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "Upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }

    # REST API & Static Client
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        
        # Required for Godot 4 Web multithreading
        add_header Cross-Origin-Opener-Policy "same-origin";
        add_header Cross-Origin-Embedder-Policy "require-corp";
    }
}
```

---

## Client Connection

### Desktop Client
To connect the desktop client to your cloud server:
```powershell
.\play.cmd --server=wss://angband.example.com/ws
```
Or toggle in-game:
**Main Menu** $\rightarrow$ `Engine Mode: [Cloud Realm]`

### Web Browser Client
Navigate to `https://angband.example.com/` — the client runs directly in your browser with zero install required.
