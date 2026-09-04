# AB-1007 CI fix: adds SQL Server Full-Text Search (mssql-server-fts) to the official SQL Server
# 2022 Linux image. The base mcr.microsoft.com/mssql/server:2022-latest image does NOT include it
# — confirmed by a real CI failure (Error 7609, "Full-Text Search is not installed, or a
# full-text component cannot be loaded" on CREATE FULLTEXT INDEX, see
# openspec/changes/ab-1007-search/tasks.md Phase 4 "CI verification"). Full-Text Search must be
# installed before sqlservr's first start
# (learn.microsoft.com/en-us/sql/linux/install-upgrade/setup-full-text-search) — there is no way
# to add it to an already-running container's engine — so this must happen at image-build time,
# which is also why the "backend" CI job builds this image and runs it as a normal container step
# instead of a GitHub Actions `services:` container (services: only pulls a pre-built image and
# starts it before any job step runs; it cannot run an install step first).
#
# Recipe: Microsoft's own mssql-server-fts install steps
# (learn.microsoft.com/en-us/sql/linux/install-upgrade/setup-full-text-search#tabpanel_1_ubuntu)
# plus the Microsoft package-repo/key setup that makes the mssql-server-fts package visible to
# apt on a machine that didn't go through SQL Server's own bare-metal installer.
FROM mcr.microsoft.com/mssql/server:2022-latest

USER root

RUN apt-get update \
    && apt-get install -y curl apt-transport-https gnupg \
    && curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - \
    && curl https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list | tee /etc/apt/sources.list.d/mssql-server-2022.list \
    && apt-get update \
    && apt-get install -y mssql-server-fts \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* /*.deb

USER mssql

EXPOSE 1433

CMD ["/opt/mssql/bin/sqlservr"]
