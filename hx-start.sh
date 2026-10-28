#!/usr/bin/env bash
# ============================================================================
# 华夏 MMORPG 服务端一键启动脚本
# 用法: ./hx-start.sh          # 全量编译 + 启动
#       ./hx-start.sh --quick  # 只启动，跳过编译
#       ./hx-start.sh --mock   # 编译 + 启动 + 自动跑 MockClient 测 Gate
# ============================================================================

set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# ---------- 颜色 ----------
R='\033[0;31m'; G='\033[0;32m'; Y='\033[1;33m'; C='\033[0;36m'; N='\033[0m'
ok()  { echo -e "  ${G}✓${N} $*"; }
info(){ echo -e "  ${C}→${N} $*"; }
warn(){ echo -e "  ${Y}!${N} $*"; }
err() { echo -e "  ${R}✗${N} $*"; exit 1; }

# ---------- 参数 ----------
SKIP_BUILD=false
RUN_MOCK=false
for arg in "$@"; do
  case $arg in
    --quick) SKIP_BUILD=true ;;
    --mock)  RUN_MOCK=true ;;
  esac
done

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  华夏 MMORPG 服务端启动脚本  (ET10 macOS arm64)           ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ---------- 1. 检查 MongoDB ----------
info "检查 MongoDB..."
if docker ps --format '{{.Names}}' | grep -q '^hx-mongodb$'; then
  ok "hx-mongodb 运行中"
else
  warn "MongoDB 未启动，尝试启动 hx-mongodb..."
  if docker ps -a --format '{{.Names}}' | grep -q '^hx-mongodb$'; then
    docker start hx-mongodb >/dev/null 2>&1 && ok "hx-mongodb 已启动" || err "启动失败"
  else
    info "新建 hx-mongodb 容器..."
    docker run -d --name hx-mongodb -p 27017:27017 mongo:7 >/dev/null 2>&1 \
      && ok "hx-mongodb 创建并启动" || err "创建失败"
  fi
fi
sleep 1

# ---------- 2. 检查 dotnet ----------
info "检查 .NET SDK..."
command -v dotnet >/dev/null 2>&1 || err "dotnet 未安装"
DOTNET_VER=$(dotnet --version)
[[ "$DOTNET_VER" =~ ^10 ]] || warn ".NET SDK $DOTNET_VER (推荐 10.x)"
ok "dotnet $DOTNET_VER"

# ---------- 3. 生成 MainPackage.txt ----------
if [ ! -f MainPackage.txt ]; then
  info "生成 MainPackage.txt..."
  pwsh Scripts/Initialize-Project.ps1 -SceneName StateSync -GenerateMainPackageOnly >/dev/null 2>&1 \
    && ok "MainPackage.txt 已生成" || err "MainPackage.txt 生成失败"
fi

# ---------- 4. 编译 (Model → Hotfix → App) ----------
if [ "$SKIP_BUILD" = false ]; then
  echo ""
  echo "  📦 编译阶段..."
  echo ""

  info "[1/3] ET.Model..."
  dotnet build Packages/cn.etetet.statesync/DotNet~/Model/ET.Model.csproj -c Release -v q \
    || err "Model 编译失败"
  ok "ET.Model.dll"

  info "[2/3] ET.Hotfix..."
  dotnet build Packages/cn.etetet.statesync/DotNet~/Hotfix/ET.Hotfix.csproj -c Release -v q \
    || err "Hotfix 编译失败"
  ok "ET.Hotfix.dll"

  info "[3/3] ET.App..."
  dotnet build Packages/cn.etetet.statesync/DotNet~/App/ET.App.csproj -c Release -v q \
    || err "App 编译失败"
  ok "ET.App.dll"

  echo ""
  ok "编译全部通过 (0 error)"
else
  info "跳过编译 (--quick)"
fi

# ---------- 5. 停掉旧进程 ----------
PID_FILE="$SCRIPT_DIR/.hxserver.pid"
if [ -f "$PID_FILE" ]; then
  OLD_PID=$(cat "$PID_FILE")
  if kill -0 "$OLD_PID" 2>/dev/null; then
    warn "发现旧进程 PID=$OLD_PID, 正在停止..."
    kill "$OLD_PID" 2>/dev/null || true
    sleep 2
    kill -9 "$OLD_PID" 2>/dev/null || true
  fi
  rm -f "$PID_FILE"
fi

# ---------- 6. 启动服务端 ----------
echo ""
echo "  🚀 启动 ET10 StateSync..."
echo ""

APP_DLL="$SCRIPT_DIR/Packages/cn.etetet.statesync/DotNet~/App/Bin/ET.App.dll"
[ -f "$APP_DLL" ] || err "找不到 ET.App.dll, 请先编译"

LOG_FILE="/tmp/hxserver-console.log"
> "$LOG_FILE"

nohup dotnet "$APP_DLL" \
  --SceneName=StateSync \
  --Process=1 \
  --StartConfig=Localhost \
  --Console=1 \
  >"$LOG_FILE" 2>&1 < /dev/null &
SERVER_PID=$!
echo "$SERVER_PID" > "$PID_FILE"

info "等待服务端就绪 (PID=$SERVER_PID)..."
for i in $(seq 1 20); do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    err "服务端进程退出! 查看日志: $LOG_FILE"
  fi
  sleep 1
done

# ---------- 7. 验证端口 ----------
echo ""
sleep 2

check_port() {
  local port=$1 name=$2
  if lsof -iTCP:"$port" -sTCP:LISTEN -P 2>/dev/null | grep -q LISTEN; then
    ok "$name 端口 $port 监听中"
    return 0
  else
    err "$name 端口 $port 未监听!"
    return 1
  fi
}

echo "  🔌 端口验证..."
check_port 1600 "Realm"
check_port 1607 "Gate1 (赤水)"
check_port 1602 "Gate2 (雷炎)"

# ---------- 8. 完成 ----------
echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  ✅ 华夏服务端启动完成!                                   ║"
echo "╠══════════════════════════════════════════════════════════╣"
echo "║  PID:   $SERVER_PID"
echo "║  日志:  $LOG_FILE"
echo "║  停服:  ./hx-stop.sh"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ---------- 9. 可选: MockClient ----------
if [ "$RUN_MOCK" = true ]; then
  info "编译 MockClient..."
  dotnet build Packages/cn.etetet.huaxia/MockClient/MockClient.csproj -c Release -v q 2>/dev/null \
    || err "MockClient 编译失败"
  ok "MockClient.dll"
  echo ""
  echo "  🧪 运行 MockClient → Gate 1607..."
  echo ""
  cd Packages/cn.etetet.huaxia/MockClient
  dotnet run -c Release -- --port 1607 2>&1 || true
  cd "$SCRIPT_DIR"
fi
