# The Maxon compiler as an image, built from a published release:
#
#   docker build --build-arg VERSION=0.1.1 --target debian installer/docker
#
# Targets:
#   debian      debian:bookworm-slim, with a shell — for CI jobs and devcontainers. Runs as root.
#   distroless  gcr.io/distroless/static-debian12:nonroot — a few MB, no shell, runs as uid 65532.
#
# Both install the release as install.sh would, under /opt/maxon: the compiler in bin/, the standard
# library beside it. Neither sets an ENTRYPOINT, so `docker run <image> maxon …` reads the same in both.
#
# ⭐ THE IMAGE IS BUILT FROM THE RELEASE'S OWN ARCHIVE, CHECKED AGAINST ITS SHA256SUMS, so the compiler
# inside is byte-for-byte the one `install.sh` and Homebrew install.

ARG VERSION

FROM debian:bookworm-slim AS fetch
ARG VERSION
ARG TARGETARCH
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*
RUN set -eu; \
    [ -n "$VERSION" ] || { echo "--build-arg VERSION=X.Y.Z is required" >&2; exit 1; }; \
    case "$TARGETARCH" in \
      amd64) arch=x64 ;; \
      arm64) arch=arm64 ;; \
      *) echo "there is no Maxon build for linux/$TARGETARCH" >&2; exit 1 ;; \
    esac; \
    asset="maxon-$VERSION-$arch-linux.tar.gz"; \
    base="https://github.com/maxon-lang/maxon/releases/download/v$VERSION"; \
    cd /tmp; \
    curl --proto '=https' --tlsv1.2 -fsSLO "$base/$asset"; \
    curl --proto '=https' --tlsv1.2 -fsSLO "$base/SHA256SUMS"; \
    awk -v f="$asset" '{ name = $2; sub(/^\*/, "", name) } name == f { print $1 "  " f }' SHA256SUMS > expected; \
    [ -s expected ] || { echo "SHA256SUMS lists no $asset" >&2; exit 1; }; \
    sha256sum -c expected; \
    tar -xzf "$asset"; \
    mkdir -p /opt/maxon/bin; \
    mv "maxon-$VERSION-$arch-linux/maxon" /opt/maxon/bin/maxon; \
    mv "maxon-$VERSION-$arch-linux/stdlib" "maxon-$VERSION-$arch-linux/examples" /opt/maxon/
# One compile as root, here, leaves the standard library's build cache in /opt/maxon, so a compile by
# an unprivileged user in the final image reads it instead of needing to write it.
RUN set -eu; \
    /opt/maxon/bin/maxon version; \
    mkdir /tmp/warm; \
    cp /opt/maxon/examples/hello.maxon /tmp/warm/; \
    cd /tmp/warm; \
    /opt/maxon/bin/maxon build hello.maxon -o hello; \
    ./hello

FROM debian:bookworm-slim AS debian
LABEL org.opencontainers.image.source="https://github.com/maxon-lang/maxon" \
      org.opencontainers.image.description="The Maxon compiler" \
      org.opencontainers.image.licenses="MIT OR Apache-2.0"
COPY --from=fetch /opt/maxon /opt/maxon
# MAXON_IMAGE tells `maxon upgrade` it is in an image, where the upgrade is a newer image, not a new install.
ENV PATH=/opt/maxon/bin:$PATH \
    MAXON_IMAGE=ghcr.io/maxon-lang/maxon
RUN useradd --create-home --uid 1000 maxon
WORKDIR /app
CMD ["maxon"]

FROM gcr.io/distroless/static-debian12:nonroot AS distroless
LABEL org.opencontainers.image.source="https://github.com/maxon-lang/maxon" \
      org.opencontainers.image.description="The Maxon compiler, distroless" \
      org.opencontainers.image.licenses="MIT OR Apache-2.0"
COPY --from=fetch /opt/maxon /opt/maxon
ENV PATH=/opt/maxon/bin:$PATH \
    MAXON_IMAGE=ghcr.io/maxon-lang/maxon
WORKDIR /app
CMD ["maxon"]
