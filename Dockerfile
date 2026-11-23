# 仓库根目录 Dockerfile（构建上下文 = 仓库根），适用于 Zeabur / Render(根) 等平台
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY Workbench/Workbench.csproj Workbench/
RUN dotnet restore Workbench/Workbench.csproj
COPY Workbench/ Workbench/
RUN dotnet publish Workbench/Workbench.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0 \
    DATA_DIR=/data
RUN mkdir -p /data
COPY --from=build /app/publish .
# 容器平台通过 PORT 环境变量分配监听端口（见 Program.cs），无需固定 EXPOSE
ENTRYPOINT ["dotnet", "Workbench.dll"]
