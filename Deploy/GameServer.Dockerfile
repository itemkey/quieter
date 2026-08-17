FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        libatomic1 \
        libgcc-s1 \
        libstdc++6 \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --uid 10001 --shell /usr/sbin/nologin quieter
WORKDIR /server
COPY Builds/LinuxServer/ ./
RUN chmod +x /server/QuieterServer \
    && chown -R quieter:quieter /server /home/quieter

USER quieter
EXPOSE 7777/udp
ENTRYPOINT ["/server/QuieterServer", "-batchmode", "-nographics", "-logFile", "/dev/stdout"]

