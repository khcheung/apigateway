FROM mcr.microsoft.com/dotnet/sdk:6.0.301-bullseye-slim-amd64
WORKDIR /opt/console
EXPOSE 80
COPY . .
RUN dotnet restore
CMD ["dotnet","run"]
