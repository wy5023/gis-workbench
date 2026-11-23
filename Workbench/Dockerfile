FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY Workbench.csproj ./
RUN dotnet restore Workbench.csproj
COPY . .
RUN dotnet publish Workbench.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0 \
    DATA_DIR=/data
RUN mkdir -p /data
COPY --from=build /app/publish .
EXPOSE 10000
ENTRYPOINT ["dotnet", "Workbench.dll"]
