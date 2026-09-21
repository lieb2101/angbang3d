# ==============================================================================
# Angband3D Cloud Container: Multi-stage Headless Engine + WebSocket Relay
# ==============================================================================

# Stage 1: Build the Angband 4.2.6 C Engine with Bridge Protocol
FROM ubuntu:22.04 AS engine-builder

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    git \
    libncurses5-dev \
    pkg-config \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# Clone official upstream Angband 4.2.6 release tag
RUN git clone --depth 1 --branch 4.2.6 https://github.com/angband/angband.git /src/engine

# Apply Angband3D bridge frontend patch
COPY engine-patch/0001-bridge-frontend.patch /src/
WORKDIR /src/engine
RUN git apply /src/0001-bridge-frontend.patch

# Build headless Angband with bridge frontend
RUN mkdir -p build && cd build && \
    cmake .. -DCMAKE_BUILD_TYPE=Release \
             -DSUPPORT_BRIDGE_FRONTEND=ON \
             -DSUPPORT_GCU_FRONTEND=ON \
             -DSUPPORT_X11_FRONTEND=OFF \
             -DSUPPORT_SDL_FRONTEND=OFF \
             -DSUPPORT_SDL2_FRONTEND=OFF \
             -DSUPPORT_STATIC_LINKING=ON && \
    make -j$(nproc)

# Stage 2: Runtime Image with Node.js WebSocket Daemon
FROM node:18-slim AS runtime

# Install runtime libraries and UTF-8 locale support
RUN apt-get update && apt-get install -y --no-install-recommends \
    libncursesw6 \
    libncurses6 \
    libtinfo6 \
    ca-certificates \
    locales \
    && rm -rf /var/lib/apt/lists/*

ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

WORKDIR /app

# Copy compiled engine and game library
COPY --from=engine-builder /src/engine/build/game/angband /app/engine/angband
COPY --from=engine-builder /src/engine/build/game/lib /app/engine/lib

# Setup cloud server daemon
COPY server/package*.json /app/server/
WORKDIR /app/server
RUN npm install --omit=dev

COPY server/src/ /app/server/src/
COPY server/public/ /app/public/

# Prepare directories for persistent player saves, standalone dist, and web client
RUN mkdir -p /data/save /app/dist /app/public && \
    chmod +x /app/engine/angband

VOLUME /data/save

ENV PORT=8080
ENV ENGINE_EXE=/app/engine/angband
ENV SAVE_DIR=/data/save
ENV DIST_DIR=/app/dist
ENV WEB_DIR=/app/public
ENV NODE_ENV=production

EXPOSE 8080

CMD ["npm", "start"]
