#!/bin/bash

# Workshop & Gofile Daemon Management Script
# Interactive control for workshop-archiver and gofile-daemon services

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Service names
WORKSHOP_SERVICE="workshop-archiver"
GOFILE_SERVICE="gofile-daemon"

# Paths
WORKSHOP_DATA="/var/lib/workshop-archiver"
WORKSHOP_OPT="/opt/workshop-archiver"
GOFILE_DATA="/var/lib/gofile-daemon"
GOFILE_OPT="/opt/gofile-daemon"

print_header() {
    echo -e "${CYAN}"
    echo "+================================================================+"
    echo "|          Workshop & Gofile Daemon Manager                     |"
    echo "+================================================================+"
    echo -e "${NC}"
}

print_status() {
    echo -e "${BLUE}------------------------------------------------------------------${NC}"
    echo -e "${CYAN}Service Status:${NC}"
    echo ""
    
    if systemctl is-active --quiet $WORKSHOP_SERVICE; then
        echo -e "  Workshop Archiver:  ${GREEN}[Running]${NC}"
    else
        echo -e "  Workshop Archiver:  ${RED}[Stopped]${NC}"
    fi
    
    if systemctl is-active --quiet $GOFILE_SERVICE; then
        echo -e "  Gofile Daemon:      ${GREEN}[Running]${NC}"
    else
        echo -e "  Gofile Daemon:      ${RED}[Stopped]${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}Database Stats:${NC}"
    
    if [ -f "$WORKSHOP_DATA/workshop.db" ]; then
        local workshop_items=$(sqlite3 "$WORKSHOP_DATA/workshop.db" "SELECT COUNT(*) FROM WorkshopItems;" 2>/dev/null || echo "0")
        local archived_items=$(sqlite3 "$WORKSHOP_DATA/workshop.db" "SELECT COUNT(*) FROM WorkshopItems WHERE ArchivedAt IS NOT NULL;" 2>/dev/null || echo "0")
        local failed_downloads=$(sqlite3 "$WORKSHOP_DATA/workshop.db" "SELECT COUNT(*) FROM FailedDownloads;" 2>/dev/null || echo "0")
        echo -e "  Workshop Items:     ${workshop_items} total, ${archived_items} archived, ${failed_downloads} failed"
    else
        echo -e "  Workshop Items:     ${YELLOW}No database${NC}"
    fi
    
    if [ -f "$GOFILE_DATA/uploads.db" ]; then
        local uploaded_files=$(sqlite3 "$GOFILE_DATA/uploads.db" "SELECT COUNT(*) FROM UploadedFiles;" 2>/dev/null || echo "0")
        local failed_uploads=$(sqlite3 "$GOFILE_DATA/uploads.db" "SELECT COUNT(*) FROM FailedUploads;" 2>/dev/null || echo "0")
        echo -e "  Gofile Uploads:     ${uploaded_files} uploaded, ${failed_uploads} failed"
    else
        echo -e "  Gofile Uploads:     ${YELLOW}No database${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}Pending Files:${NC}"
    local pending=$(ls -1 "$GOFILE_DATA/watch/"*.7z 2>/dev/null | wc -l || echo "0")
    echo -e "  Watch Directory:    ${pending} files waiting for upload"
    
    echo -e "${BLUE}------------------------------------------------------------------${NC}"
}

print_menu() {
    echo ""
    echo -e "${YELLOW}Select an action:${NC}"
    echo ""
    echo "  Service Control:"
    echo "    1) Start both services"
    echo "    2) Stop both services"
    echo "    3) Restart both services"
    echo "    4) Start Workshop Archiver only"
    echo "    5) Start Gofile Daemon only"
    echo ""
    echo "  Logs:"
    echo "    6) View Workshop Archiver logs (live)"
    echo "    7) View Gofile Daemon logs (live)"
    echo "    8) View both logs (live)"
    echo ""
    echo "  Reset & Cleanup:"
    echo "    9) Reset Workshop Archiver (clear DB & downloads)"
    echo "   10) Reset Gofile Daemon (clear DB & uploads)"
    echo "   11) Full reset (both services)"
    echo "   12) Clear pending uploads only"
    echo "   13) Clear failed downloads only"
    echo "   14) Clear failed uploads only"
    echo ""
    echo "  Database:"
    echo "   15) Show workshop items"
    echo "   16) Show failed downloads"
    echo "   17) Show uploaded files"
    echo "   18) Show failed uploads"
    echo ""
    echo "  Other:"
    echo "   19) Refresh status"
    echo "    0) Exit"
    echo ""
}

confirm() {
    local prompt="$1"
    echo -e -n "${YELLOW}${prompt} [y/N]: ${NC}"
    read -r response
    case "$response" in
        [yY][eE][sS]|[yY]) return 0 ;;
        *) return 1 ;;
    esac
}

start_both() {
    echo -e "${GREEN}Starting both services...${NC}"
    systemctl start $GOFILE_SERVICE
    systemctl start $WORKSHOP_SERVICE
    echo -e "${GREEN}Done!${NC}"
}

stop_both() {
    echo -e "${YELLOW}Stopping both services...${NC}"
    systemctl stop $WORKSHOP_SERVICE 2>/dev/null || true
    systemctl stop $GOFILE_SERVICE 2>/dev/null || true
    echo -e "${GREEN}Done!${NC}"
}

restart_both() {
    echo -e "${YELLOW}Restarting both services...${NC}"
    systemctl restart $GOFILE_SERVICE
    systemctl restart $WORKSHOP_SERVICE
    echo -e "${GREEN}Done!${NC}"
}

view_workshop_logs() {
    echo -e "${CYAN}Viewing Workshop Archiver logs (Ctrl+C to exit)...${NC}"
    journalctl -u $WORKSHOP_SERVICE -f
}

view_gofile_logs() {
    echo -e "${CYAN}Viewing Gofile Daemon logs (Ctrl+C to exit)...${NC}"
    journalctl -u $GOFILE_SERVICE -f
}

view_both_logs() {
    echo -e "${CYAN}Viewing both logs (Ctrl+C to exit)...${NC}"
    journalctl -u $WORKSHOP_SERVICE -u $GOFILE_SERVICE -f
}

reset_workshop() {
    if ! confirm "This will DELETE all workshop data and databases. Continue?"; then
        echo "Cancelled."
        return
    fi
    
    echo -e "${YELLOW}Stopping Workshop Archiver...${NC}"
    systemctl stop $WORKSHOP_SERVICE 2>/dev/null || true
    
    echo -e "${YELLOW}Removing workshop data...${NC}"
    rm -rf "$WORKSHOP_DATA/workshop.db"
    rm -rf "$WORKSHOP_DATA/downloads"/*
    rm -rf "$WORKSHOP_OPT/depots"
    rm -rf "$WORKSHOP_OPT/.DepotDownloader"
    
    # Recreate directories
    mkdir -p "$WORKSHOP_DATA/downloads"
    chown -R workshop-archiver:workshop "$WORKSHOP_DATA"
    
    echo -e "${GREEN}Workshop Archiver reset complete!${NC}"
    
    if confirm "Start Workshop Archiver now?"; then
        systemctl start $WORKSHOP_SERVICE
    fi
}

reset_gofile() {
    if ! confirm "This will DELETE all gofile upload records. Continue?"; then
        echo "Cancelled."
        return
    fi
    
    echo -e "${YELLOW}Stopping Gofile Daemon...${NC}"
    systemctl stop $GOFILE_SERVICE 2>/dev/null || true
    
    echo -e "${YELLOW}Removing gofile data...${NC}"
    rm -rf "$GOFILE_DATA/uploads.db"
    
    echo -e "${GREEN}Gofile Daemon reset complete!${NC}"
    
    if confirm "Start Gofile Daemon now?"; then
        systemctl start $GOFILE_SERVICE
    fi
}

full_reset() {
    if ! confirm "This will DELETE ALL DATA for both services. Are you SURE?"; then
        echo "Cancelled."
        return
    fi
    
    if ! confirm "This is your last chance to cancel. Proceed with FULL RESET?"; then
        echo "Cancelled."
        return
    fi
    
    echo -e "${RED}Performing full reset...${NC}"
    
    # Stop both services
    echo -e "${YELLOW}Stopping services...${NC}"
    systemctl stop $WORKSHOP_SERVICE 2>/dev/null || true
    systemctl stop $GOFILE_SERVICE 2>/dev/null || true
    
    # Clear workshop data
    echo -e "${YELLOW}Clearing Workshop Archiver data...${NC}"
    rm -rf "$WORKSHOP_DATA/workshop.db"
    rm -rf "$WORKSHOP_DATA/downloads"/*
    rm -rf "$WORKSHOP_OPT/depots"
    rm -rf "$WORKSHOP_OPT/.DepotDownloader"
    mkdir -p "$WORKSHOP_DATA/downloads"
    chown -R workshop-archiver:workshop "$WORKSHOP_DATA"
    
    # Clear gofile data
    echo -e "${YELLOW}Clearing Gofile Daemon data...${NC}"
    rm -rf "$GOFILE_DATA/uploads.db"
    
    # Clear pending uploads
    echo -e "${YELLOW}Clearing pending uploads...${NC}"
    rm -rf "$GOFILE_DATA/watch"/*.7z 2>/dev/null || true
    
    echo -e "${GREEN}Full reset complete!${NC}"
    
    if confirm "Start both services now?"; then
        start_both
    fi
}

clear_pending() {
    local count=$(ls -1 "$GOFILE_DATA/watch/"*.7z 2>/dev/null | wc -l || echo "0")
    
    if [ "$count" -eq 0 ]; then
        echo -e "${YELLOW}No pending files to clear.${NC}"
        return
    fi
    
    if ! confirm "Delete $count pending .7z files from watch directory?"; then
        echo "Cancelled."
        return
    fi
    
    rm -f "$GOFILE_DATA/watch"/*.7z
    echo -e "${GREEN}Cleared $count pending files.${NC}"
}

clear_failed_downloads() {
    if [ ! -f "$WORKSHOP_DATA/workshop.db" ]; then
        echo -e "${YELLOW}No workshop database found.${NC}"
        return
    fi
    
    local count=$(sqlite3 "$WORKSHOP_DATA/workshop.db" "SELECT COUNT(*) FROM FailedDownloads;" 2>/dev/null || echo "0")
    
    if [ "$count" -eq 0 ]; then
        echo -e "${YELLOW}No failed downloads to clear.${NC}"
        return
    fi
    
    if ! confirm "Clear $count failed download records?"; then
        echo "Cancelled."
        return
    fi
    
    sqlite3 "$WORKSHOP_DATA/workshop.db" "DELETE FROM FailedDownloads;"
    echo -e "${GREEN}Cleared $count failed download records.${NC}"
}

clear_failed_uploads() {
    if [ ! -f "$GOFILE_DATA/uploads.db" ]; then
        echo -e "${YELLOW}No gofile database found.${NC}"
        return
    fi
    
    local count=$(sqlite3 "$GOFILE_DATA/uploads.db" "SELECT COUNT(*) FROM FailedUploads;" 2>/dev/null || echo "0")
    
    if [ "$count" -eq 0 ]; then
        echo -e "${YELLOW}No failed uploads to clear.${NC}"
        return
    fi
    
    if ! confirm "Clear $count failed upload records?"; then
        echo "Cancelled."
        return
    fi
    
    sqlite3 "$GOFILE_DATA/uploads.db" "DELETE FROM FailedUploads;"
    echo -e "${GREEN}Cleared $count failed upload records.${NC}"
}

show_workshop_items() {
    if [ ! -f "$WORKSHOP_DATA/workshop.db" ]; then
        echo -e "${YELLOW}No workshop database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Workshop Items (last 20):${NC}"
    sqlite3 -header -column "$WORKSHOP_DATA/workshop.db" \
        "SELECT PublishedFileId, substr(Title, 1, 40) as Title, 
                CASE WHEN ArchivedAt IS NOT NULL THEN 'Yes' ELSE 'No' END as Archived
         FROM WorkshopItems 
         ORDER BY PublishedFileId DESC 
         LIMIT 20;"
}

show_failed_downloads() {
    if [ ! -f "$WORKSHOP_DATA/workshop.db" ]; then
        echo -e "${YELLOW}No workshop database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Failed Downloads:${NC}"
    sqlite3 -header -column "$WORKSHOP_DATA/workshop.db" \
        "SELECT PublishedFileId, Attempts, substr(LastError, 1, 50) as Error, LastAttempt 
         FROM FailedDownloads 
         ORDER BY LastAttempt DESC;"
}

show_uploaded_files() {
    if [ ! -f "$GOFILE_DATA/uploads.db" ]; then
        echo -e "${YELLOW}No gofile database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Uploaded Files (last 20):${NC}"
    sqlite3 -header -column "$GOFILE_DATA/uploads.db" \
        "SELECT FileName, GofileId, UploadedAt 
         FROM UploadedFiles 
         ORDER BY UploadedAt DESC 
         LIMIT 20;"
}

show_failed_uploads() {
    if [ ! -f "$GOFILE_DATA/uploads.db" ]; then
        echo -e "${YELLOW}No gofile database found.${NC}"
        return
    fi
    
    echo -e "${CYAN}Failed Uploads:${NC}"
    sqlite3 -header -column "$GOFILE_DATA/uploads.db" \
        "SELECT FileName, Attempts, substr(LastError, 1, 50) as Error, LastAttempt 
         FROM FailedUploads 
         ORDER BY LastAttempt DESC;"
}

# Main loop
main() {
    # Check if running as root
    if [ "$EUID" -ne 0 ]; then
        echo -e "${RED}Please run as root${NC}"
        exit 1
    fi
    
    while true; do
        clear
        print_header
        print_status
        print_menu
        
        echo -n "Enter choice: "
        read -r choice
        
        case $choice in
            1) start_both ;;
            2) stop_both ;;
            3) restart_both ;;
            4) systemctl start $WORKSHOP_SERVICE && echo -e "${GREEN}Started!${NC}" ;;
            5) systemctl start $GOFILE_SERVICE && echo -e "${GREEN}Started!${NC}" ;;
            6) view_workshop_logs ;;
            7) view_gofile_logs ;;
            8) view_both_logs ;;
            9) reset_workshop ;;
            10) reset_gofile ;;
            11) full_reset ;;
            12) clear_pending ;;
            13) clear_failed_downloads ;;
            14) clear_failed_uploads ;;
            15) show_workshop_items ;;
            16) show_failed_downloads ;;
            17) show_uploaded_files ;;
            18) show_failed_uploads ;;
            19) continue ;;
            0) echo "Goodbye!"; exit 0 ;;
            *) echo -e "${RED}Invalid option${NC}" ;;
        esac
        
        echo ""
        echo -n "Press Enter to continue..."
        read -r
    done
}

main "$@"
