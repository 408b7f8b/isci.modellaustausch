#FROM mcr.microsoft.com/dotnet/runtime:8.0
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled-extra

# Working directory anlegen, Dateien kopieren und Berechtigungen setzen
WORKDIR /app
COPY ./tmp ./

# Umgebungsvariablen setzen
ENV "ISCI_OrdnerAnwendungen"="/app/Anwendungen"
ENV "ISCI_OrdnerDatenstrukturen"="/app/Datenstrukturen"

# Umgebungsvariablen, die auf dem System angelegt werden müssen:
# ISCI_Identifikation=XXX
# ISCI_Ressource=XXX
# ISCI_Anwendung=XXX
#
# Die beiden Ordner in den Umgebungsvariablen vom Host-System müssen eingebunden werden
# Es muss außerdem eine Konfiguration ${ISCI_Identifikation}.json im Ordner "${ISCI_OrdnerAnwendungen}/${ISCI_Anwendung}/Konfigurationen" vorhanden sein
# 
# Ports die durchgereicht werden müssen:
# ISCI_Port

ENTRYPOINT ["./isci.modellaustausch"]
