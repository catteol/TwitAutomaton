FROM mcr.microsoft.com/dotnet/sdk:6.0

RUN mkdir /app
WORKDIR /app

# host user
ARG UID=1000
ARG GID=1000
RUN useradd -m -u ${UID} docker
USER ${UID}:${GID}