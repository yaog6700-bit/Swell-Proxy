#!/bin/bash
# =========================================
# 描述: Snell V4/V5/V6 多版本一键安装管理脚本
# =========================================

SCRIPT_VERSION="v1.0"

# --- 颜色定义 ---
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
RESET='\033[0m'

# --- 检查 root 权限 ---
if [ "$(id -u)" != "0" ]; then
    echo -e "${RED}错误: 请使用 root 用户运行此脚本${RESET}"
    exit 1
fi

# --- 1. 安装必要依赖 ---
install_dependencies() {
    echo -e "${CYAN}正在检查并安装所需依赖 (wget, curl, unzip, c-ares, libsodium)...${RESET}"
    if [ -x "$(command -v apt)" ]; then
        apt update && apt install -y wget curl unzip libc-ares2 libsodium23
    elif [ -x "$(command -v yum)" ]; then
        yum install -y wget curl unzip c-ares libsodium
    else
        echo -e "${YELLOW}警告: 不支持的包管理器，请手动确认已安装 wget, curl, unzip, c-ares 和 libsodium。${RESET}"
    fi
}

# --- 2. 获取系统架构 ---
get_arch() {
    ARCH=$(uname -m)
    case ${ARCH} in
        "x86_64"|"amd64")
            SNELL_ARCH="amd64"
            ;;
        "aarch64"|"arm64")
            SNELL_ARCH="aarch64"
            ;;
        "i386"|"i686")
            SNELL_ARCH="i386"
            ;;
        "armv7l"|"armv7")
            SNELL_ARCH="armv7l"
            ;;
        *)
            echo -e "${RED}不支持的系统架构: ${ARCH}${RESET}"
            exit 1
            ;;
    esac
}

# --- 3. 动态获取最新版本号 ---
get_versions() {
    echo -e "${CYAN}正在获取 Snell 最新版本信息...${RESET}"
    
    # 根据官方文档设定的准确兜底版本号
    V4_VER="v4.1.1"
    V5_VER="v5.0.1" 
    V6_VER="v6.0.0b1"

    # 尝试在线抓取最新版本 (通过 Surge 官方文档)
    FETCH_V4=$(curl -sL https://manual.nssurge.com/others/snell.html | grep -oP 'snell-server-v\K4\.[0-9]+\.[0-9]+' | head -n 1)
    if [ -n "$FETCH_V4" ]; then V4_VER="v${FETCH_V4}"; fi

    FETCH_V5=$(curl -sL https://manual.nssurge.com/others/snell.html | grep -oP 'snell-server-v\K5\.[0-9]+\.[0-9]+[a-z0-9]*' | grep -v b | head -n 1)
    if [ -n "$FETCH_V5" ]; then V5_VER="v${FETCH_V5}"; fi

    FETCH_V6=$(curl -sL https://manual.nssurge.com/others/snell.html | grep -oP 'snell-server-v\K6\.[0-9]+\.[0-9]+[a-z0-9]*' | head -n 1)
    if [ -n "$FETCH_V6" ]; then V6_VER="v${FETCH_V6}"; fi
}

# --- 4. 核心安装逻辑 ---
install_snell() {
    local VERSION_TYPE=$1
    local VERSION_NUM=""

    case $VERSION_TYPE in
        4) VERSION_NUM=$V4_VER ;;
        5) VERSION_NUM=$V5_VER ;;
        6) 
            VERSION_NUM=$V6_VER 
            # 官方 V6 目前没有提供 armv7l 版本，进行拦截
            if [ "$SNELL_ARCH" = "armv7l" ]; then
                echo -e "${RED}错误: Snell V6 官方目前暂未提供 armv7l 架构的安装包。${RESET}"
                echo -e "${YELLOW}建议安装 V5 或 V4 版本。${RESET}"
                exit 1
            fi
            ;;
    esac

    echo -e "${CYAN}准备安装 Snell ${VERSION_NUM} (${SNELL_ARCH})...${RESET}"

    SNELL_URL="https://dl.nssurge.com/snell/snell-server-${VERSION_NUM}-linux-${SNELL_ARCH}.zip"

    echo -e "${CYAN}正在下载: ${SNELL_URL}${RESET}"
    wget -O snell.zip "$SNELL_URL"
    if [ $? -ne 0 ]; then
        echo -e "${RED}下载失败，请检查网络或该架构的版本是否在官方存在！${RESET}"
        rm -f snell.zip
        exit 1
    fi

    # 停止老旧服务
    systemctl stop snell 2>/dev/null
    
    unzip -o snell.zip -d /usr/local/bin/
    rm -f snell.zip
    chmod +x /usr/local/bin/snell-server

    # --- 配置生成 ---
    CONF_DIR="/etc/snell"
    CONF_FILE="$CONF_DIR/snell-server.conf"

    mkdir -p $CONF_DIR

    if [ -f "$CONF_FILE" ]; then
        echo -e "${YELLOW}检测到已存在配置文件 ($CONF_FILE)，将保留原有配置。\033[0m"
        echo -e "${YELLOW}如需修改端口或密码，请在主菜单选择 [5. 查看/修改配置]。\033[0m"
    else
        RANDOM_PORT=$(shuf -i 10000-65000 -n 1)
        RANDOM_PSK=$(tr -dc A-Za-z0-9 </dev/urandom | head -c 20)
        
        echo -e ""
        read -p "请输入 Snell 端口号 [1-65535] (直接回车默认随机: $RANDOM_PORT): " USER_PORT < /dev/tty
        if [ -z "$USER_PORT" ]; then
            USER_PORT=$RANDOM_PORT
        fi

        read -p "请输入 Snell 密码 (直接回车默认随机: $RANDOM_PSK): " USER_PSK < /dev/tty
        if [ -z "$USER_PSK" ]; then
            USER_PSK=$RANDOM_PSK
        fi
        
        cat > $CONF_FILE <<EOF
[snell-server]
listen = 0.0.0.0:$USER_PORT
psk = $USER_PSK
ipv6 = false
EOF
        echo -e "${GREEN}已生成全新配置文件！${RESET}"
    fi

    # --- systemd 服务设置 ---
    cat > /etc/systemd/system/snell.service <<EOF
[Unit]
Description=Snell Proxy Service
After=network.target

[Service]
Type=simple
LimitNOFILE=32768
ExecStart=/usr/local/bin/snell-server -c $CONF_FILE
StandardOutput=syslog
StandardError=syslog
SyslogIdentifier=snell-server
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable snell
    systemctl restart snell

    if systemctl is-active --quiet snell; then
        echo -e "\n${GREEN}=========================================${RESET}"
        echo -e "${GREEN}  Snell ${VERSION_NUM} 安装并启动成功！${RESET}"
        echo -e "${GREEN}=========================================${RESET}"
        cat $CONF_FILE
        echo -e "${GREEN}=========================================${RESET}"

        # 获取公网IP并从配置文件读取端口和密码生成 Surge 配置
        PUBLIC_IP=$(curl -s4 ifconfig.me 2>/dev/null || curl -s4 icanhazip.com 2>/dev/null)
        CONF_PORT=$(grep 'listen' $CONF_FILE | awk -F ':' '{print $NF}' | tr -d ' ')
        CONF_PSK=$(grep 'psk' $CONF_FILE | awk -F '=' '{print $2}' | tr -d ' ')
        
        if [ -n "$PUBLIC_IP" ] && [ -n "$CONF_PORT" ] && [ -n "$CONF_PSK" ]; then
            echo -e "\n${YELLOW}>>> Surge 客户端配置格式 (供一键复制) <<<${RESET}"
            case $VERSION_TYPE in
                4) SURGE_VER=4 ;;
                5) SURGE_VER=5 ;;
                6) SURGE_VER=6 ;;
            esac
            echo -e "Snell${SURGE_VER} = snell, ${PUBLIC_IP}, ${CONF_PORT}, psk = ${CONF_PSK}, version = ${SURGE_VER}, reuse = true, tfo = true"
            echo -e "${GREEN}=========================================${RESET}\n"
        fi

        echo -e "服务状态查询: ${CYAN}systemctl status snell${RESET}"
        echo -e "实时日志查询: ${CYAN}journalctl -u snell -f${RESET}"
    else
        echo -e "${RED}警告：服务启动失败，请检查日志 (journalctl -u snell -f)。${RESET}"
    fi
}

# --- 5. 卸载逻辑 ---
uninstall_snell() {
    echo -e "${CYAN}正在卸载 Snell...${RESET}"
    systemctl stop snell 2>/dev/null
    systemctl disable snell 2>/dev/null
    rm -f /etc/systemd/system/snell.service
    rm -f /usr/local/bin/snell-server
    systemctl daemon-reload
    
    read -p "是否删除配置文件和密码？[y/N]: " DEL_CONF
    if [[ "$DEL_CONF" == "y" || "$DEL_CONF" == "Y" ]]; then
        rm -rf /etc/snell
        echo -e "${GREEN}已删除配置！${RESET}"
    else
        echo -e "${YELLOW}已保留配置文件。${RESET}"
    fi
    echo -e "${GREEN}Snell 卸载完成！${RESET}"
}

# --- 6. 查看/修改配置 ---
modify_config() {
    CONF_DIR="/etc/snell"
    CONF_FILE="$CONF_DIR/snell-server.conf"
    
    if [ ! -f "$CONF_FILE" ]; then
        echo -e "${RED}未检测到 Snell 配置文件，请先安装 Snell！${RESET}"
        return
    fi
    
    echo -e "\n${CYAN}当前配置文件内容：${RESET}"
    cat $CONF_FILE
    echo -e "${CYAN}=================================${RESET}"
    
    read -p "是否需要修改端口和密码？[y/N]: " modify_choice
    if [[ "$modify_choice" == "y" || "$modify_choice" == "Y" ]]; then
        RANDOM_PORT=$(shuf -i 10000-65000 -n 1)
        RANDOM_PSK=$(tr -dc A-Za-z0-9 </dev/urandom | head -c 20)
        
        echo -e ""
        read -p "请输入新的 Snell 端口号 [1-65535] (直接回车默认随机: $RANDOM_PORT): " USER_PORT < /dev/tty
        if [ -z "$USER_PORT" ]; then
            USER_PORT=$RANDOM_PORT
        fi

        read -p "请输入新的 Snell 密码 (直接回车默认随机: $RANDOM_PSK): " USER_PSK < /dev/tty
        if [ -z "$USER_PSK" ]; then
            USER_PSK=$RANDOM_PSK
        fi
        
        cat > $CONF_FILE <<EOF
[snell-server]
listen = 0.0.0.0:$USER_PORT
psk = $USER_PSK
ipv6 = false
EOF
        echo -e "${GREEN}配置文件已更新！正在重启服务...${RESET}"
        systemctl restart snell
        if systemctl is-active --quiet snell; then
            echo -e "${GREEN}服务重启成功，新配置已生效！${RESET}"
            
            # 输出新的 Surge 配置
            PUBLIC_IP=$(curl -s4 ifconfig.me 2>/dev/null || curl -s4 icanhazip.com 2>/dev/null)
            if [ -n "$PUBLIC_IP" ]; then
                echo -e "\n${YELLOW}>>> 最新 Surge 客户端配置格式 <<<${RESET}"
                # 尝试检测当前运行的版本
                SURGE_VER="4"
                if snell-server --version 2>&1 | grep -q "v5"; then SURGE_VER=5; fi
                if snell-server --version 2>&1 | grep -q "v6"; then SURGE_VER=6; fi
                echo -e "Snell${SURGE_VER} = snell, ${PUBLIC_IP}, ${USER_PORT}, psk = ${USER_PSK}, version = ${SURGE_VER}, reuse = true, tfo = true"
                echo -e "${GREEN}=========================================${RESET}\n"
            fi
        else
            echo -e "${RED}警告：服务重启失败，请检查配置。${RESET}"
        fi
    else
        echo -e "${YELLOW}已取消修改。${RESET}"
    fi
}

# --- 7. 交互式菜单 ---
menu() {
    clear
    echo -e "${CYAN}=================================${RESET}"
    echo -e "${GREEN}  Snell 代理一键安装管理脚本 [${SCRIPT_VERSION}]${RESET}"
    echo -e "${YELLOW}  支持版本: V4 / V5 / V6${RESET}"
    echo -e "${CYAN}=================================${RESET}"
    echo -e "  1. 安装/更新 Snell V4"
    echo -e "  2. 安装/更新 Snell V5"
    echo -e "  3. 安装/更新 Snell V6 (推荐)"
    echo -e "  4. 完全卸载 Snell"
    echo -e "  5. 查看/修改配置 (端口和密码)"
    echo -e "  0. 退出脚本"
    echo -e "${CYAN}=================================${RESET}"
    
    read -p "请输入数字选项 [0-5]: " choice

    case $choice in
        1)
            get_arch
            get_versions
            install_dependencies
            install_snell 4
            ;;
        2)
            get_arch
            get_versions
            install_dependencies
            install_snell 5
            ;;
        3)
            get_arch
            get_versions
            install_dependencies
            install_snell 6
            ;;
        4)
            uninstall_snell
            ;;
        5)
            modify_config
            ;;
        0)
            echo -e "${GREEN}退出脚本，感谢使用！${RESET}"
            exit 0
            ;;
        *)
            echo -e "${RED}无效选项，请重新运行脚本输入正确的数字。${RESET}"
            ;;
    esac
}

# --- 启动 ---
menu
