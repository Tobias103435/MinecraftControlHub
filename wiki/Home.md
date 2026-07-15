# Nexora Launcher — Wiki

Welcome to the Nexora Launcher wiki. Use the sidebar to navigate between topics, or start with one of the pages below.

---

## Getting Started

| Page | Description |
|---|---|
| [Getting Started Guide](Getting-Started-Guide) | Installation, first steps, and basic navigation |
| [Project Overview](Project-Overview) | Architecture, component map, and feature overview |
| [Configuration and Settings Management](Configuration-and-Settings-Management) | Settings schema, theme system, file paths |
| [Health Monitoring and Diagnostics](Health-Monitoring-and-Diagnostics) | Health scores, RAM analysis, diagnostics log |
| [Security and Authentication](Security-and-Authentication) | OAuth flows, 2FA, token management, server hardening |
| [Troubleshooting and FAQ](Troubleshooting-and-FAQ) | Common issues, log locations, error reference |

---

## Architecture

| Page | Description |
|---|---|
| [Architecture Overview](Architecture-Overview) | Layered architecture, MVVM, DI, service map |
| [System Design & Architecture Patterns](System-Design-and-Architecture-Patterns) | Design decisions and patterns |
| [Dependency Injection & Service Container](Dependency-Injection-and-Service-Container) | DI registration, lifetimes, resolution flow |
| [MVVM Pattern Implementation](MVVM-Pattern-Implementation) | ViewModelBase, RelayCommand, data binding |
| [Service Layer Architecture](Service-Layer-Architecture) | All Core services, interfaces, and responsibilities |
| [External API Integration Patterns](External-API-Integration-Patterns) | HTTP clients, auth flows, retry, JSON patterns |

---

## Core Services

| Page | Description |
|---|---|
| [Core Services](Core-Services) | Overview of all core service interfaces |
| [Installation Management Service](Installation-Management-Service) | Create, migrate, import, and launch installations |
| [Server Management Service](Server-Management-Service) | Server lifecycle, provisioning, crash detection |
| [Mod Management Service](Mod-Management-Service) | Search, install, dependencies, updates |
| [Account Management Service](Account-Management-Service) | Microsoft OAuth, Nexora login, 2FA, friends |
| [Settings and Configuration Service](Settings-and-Configuration-Service) | Settings persistence, theme, paths |
| [Utility and Helper Services](Utility-and-Helper-Services) | Health check, RAM calculator, logging, AppPaths |

---

## AI Command Processing System

| Page | Description |
|---|---|
| [AI Command Processing System](AI-Command-Processing-System) | End-to-end pipeline overview |
| [AI Architecture and Design](AI-Architecture-and-Design) | Components, boundaries, streaming design |
| [AI Terminal Interface](AI-Terminal-Interface) | Message model, confirmation cards, UI thread safety |
| [Knowledge Base System](Knowledge-Base-System) | Static knowledge files and live context |
| [Natural Language Command Processing](Natural-Language-Command-Processing) | NLP pipeline, parsing, conversation history |
| [Command Parsing and Validation](Command-Parsing-and-Validation) | Action routing, parameter validation |
| [Streaming Response Processing](Streaming-Response-Processing) | SSE streaming, JSON stripping, token handling |
| [Conversation History and Context Management](Conversation-History-and-Context-Management) | History window, context assembly |
| [Knowledge Base Integration](Knowledge-Base-Integration) | How knowledge files are loaded and injected |
| [Dynamic Context Integration](Dynamic-Context-Integration) | Live installation/server snapshots |
| [Static Knowledge Files](Static-Knowledge-Files) | CommandKnowledge, ModKnowledge, MinecraftKnowledge |

---

## Network Tunneling System

| Page | Description |
|---|---|
| [Network Tunneling System](Network-Tunneling-System) | Tunnel subsystem overview |
| [Tunnel Provider Architecture](Tunnel-Provider-Architecture) | Provider model, address discovery modes |
| [Tunnel Session Management](Tunnel-Session-Management) | Session lifecycle, process management, events |
| [Port Detection and Management](Port-Detection-and-Management) | PortScanService, server.properties scanning |
| [Supported Tunnel Providers](Supported-Tunnel-Providers) | All providers at a glance |
| [playit.gg Provider](playit.gg-Provider) | Free tier manual + premium stdout parsing |
| [playit Pro Provider](playit-Pro-Provider) | Premium dedicated IP and custom domain |
| [ngrok Providers (Free & Pro)](ngrok-Providers) | ngrok free and Pro tiers |
| [bore.pub Provider](bore.pub-Provider) | Lightweight no-auth tunnel |
| [serveo.net Provider](serveo.net-Provider) | SSH-based tunnel |
| [FRP Provider](FRP-Provider) | Self-hosted frp tunnel |

---

## Nexora Platform Integration

| Page | Description |
|---|---|
| [Nexora Platform Integration](Nexora-Platform-Integration) | Overview: auth, social, sharing, notifications |
| [Authentication and Security](Nexora-Authentication-and-Security) | Token flows, 2FA, Minecraft linking |
| [Desktop Integration Features](Desktop-Integration-Features) | WPF app startup, login window, share dialogs |
| [Nexora API Reference](Nexora-API-Reference) | All Launcher API endpoints |
| [User Management API](User-Management-API) | Profile, validate, link/unlink |
| [Friend System API](Friend-System-API) | Friend requests, friends list |
| [Content Sharing API](Content-Sharing-API) | Instance and tunnel sharing |
| [Notification API](Notification-API) | Notification polling and read state |
| [Real-time Communication System](Real-time-Communication-System) | Socket.IO chat server overview |
| [Socket Server Architecture](Socket-Server-Architecture) | Socket server internals |

---

## Content Management System

| Page | Description |
|---|---|
| [Content Management System](Content-Management-System) | Resource packs, shaders, worlds overview |
| [Content Browsing and Discovery](Content-Browsing-and-Discovery) | Search and install from Modrinth |
| [Asset Management and Organization](Asset-Management-and-Organization) | File layout, enable/disable, version switching |
| [Modpack Export and Import](Modpack-Export-and-Import) | `.mrpack` export, import, share codes |

---

## User Interface Architecture

| Page | Description |
|---|---|
| [User Interface Architecture](User-Interface-Architecture) | UI structure, navigation, components |
| [Main Window and Navigation System](Main-Window-and-Navigation-System) | MainWindow, sidebar, page caching |
| [Core Application Pages](Core-Application-Pages) | Home, Servers, Mods, AI, Tunnel pages |
| [Dialog Windows and Specialized Interfaces](Dialog-Windows-and-Specialized-Interfaces) | Installation settings, share windows, login |
| [Reusable UI Components](Reusable-UI-Components) | Shared controls and styles |
| [ViewModel Architecture and Data Binding](ViewModel-Architecture-and-Data-Binding) | ViewModelBase, bindings, commands |
| [Social Features Pages](Social-Features-Pages) | Friends page, account page |
| [Settings and Configuration Pages](Settings-and-Configuration-Pages) | Settings page and partials |

---

## API Reference

| Page | Description |
|---|---|
| [API Reference](API-Reference) | Internal and external API overview |
| [Data Models and Schemas](Data-Models-and-Schemas) | All data model definitions |
| [Internal Service Interfaces](Internal-Service-Interfaces) | Service interface contracts |
| [External API Integrations](External-API-Integrations) | Modrinth, Mojang, Microsoft, Nexora |
