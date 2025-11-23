FROM ubuntu:24.04

# Avoid prompts from apt
ENV DEBIAN_FRONTEND=noninteractive

# Install base dependencies first (including ca-certificates for HTTPS)
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    ninja-build \
    git \
    wget \
    curl \
    ca-certificates \
    gnupg \
    jq \
    python3 \
    python3-pip \
    bash \
    && rm -rf /var/lib/apt/lists/*

# Add LLVM 21 repository (after ca-certificates is installed)
RUN wget -qO- https://apt.llvm.org/llvm-snapshot.gpg.key | tee /etc/apt/trusted.gpg.d/apt.llvm.org.asc \
    && echo "deb http://apt.llvm.org/noble/ llvm-toolchain-noble-21 main" > /etc/apt/sources.list.d/llvm-21.list

# Install LLVM 21
RUN apt-get update && apt-get install -y \
    llvm-21 \
    llvm-21-dev \
    llvm-21-tools \
    clang-21 \
    lld-21 \
    liblld-21-dev \
    libpolly-21-dev \
    libzstd-dev \
    libxml2-dev \
    && rm -rf /var/lib/apt/lists/*

# Create a non-root user
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID

RUN if getent group $USER_GID >/dev/null 2>&1; then \
        groupmod -n $USERNAME $(getent group $USER_GID | cut -d: -f1); \
    else \
        groupadd --gid $USER_GID $USERNAME; \
    fi \
    && if getent passwd $USER_UID >/dev/null 2>&1; then \
        existing_user=$(getent passwd $USER_UID | cut -d: -f1); \
        usermod -l $USERNAME -d /home/$USERNAME -m $existing_user 2>/dev/null || true; \
    else \
        useradd --uid $USER_UID --gid $USER_GID -m $USERNAME; \
    fi \
    && apt-get update \
    && apt-get install -y sudo \
    && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME

# Switch to non-root user
USER $USERNAME

# Set working directory
WORKDIR /workspace
