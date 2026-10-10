# CaresPharma Insights

An enterprise-grade AI Analytics Copilot for CaresPharma. This project provides a conversational interface to query, analyze, and visualize business data using natural language, seamlessly bridging the gap between raw database metrics and actionable business insights.

##  Features

- **Conversational Analytics:** Chat with your data using natural language.
- **Dynamic Data Synchronization:** A robust background worker (`DataSyncWorker`) that automatically syncs schema and data from SQL Server to PostgreSQL, filtering out sensitive columns dynamically.
- **AI-Powered Queries:** Uses **Semantic Kernel** to translate natural language into read-only SQL projections and securely execute them.
- **Markdown Rendering:** Displays data clearly in the UI using beautifully formatted markdown tables and lists.
- **Clean Architecture:** Built strictly following Onion/Clean Architecture principles for maximum scalability and maintainability.

## 🛠️ Technology Stack

**Backend (Server)**
- **Framework:** .NET 10 (ASP.NET Core Web API)
- **AI Orchestration:** Microsoft Semantic Kernel
- **LLM Provider:** OpenRouter (GPT-4o-mini / Llama 3)
- **Database Access:** Dapper (Micro-ORM)
- **Databases:** SQL Server (Source) & Neon PostgreSQL (Analytics Target)
- **Architecture:** Clean Architecture (`Api`, `Application`, `Domain`, `Infrastructure`)

**Frontend (Chat UI)**
- **Framework:** Blazor WebAssembly (.NET 10)
- **Styling:** Custom CSS with Modern UI (Cyan & Slate palette, glassmorphism)
- **Markdown Parser:** Markdig

## 📁 Project Structure

```text
CaresPharma-Insights/
├── Server/
│   ├── CaresPharma-Insights.Domain/         # Core Interfaces and Entities
│   ├── CaresPharma-Insights.Application/    # AI Plugins (SqlAnalyticsPlugin, etc.)
│   ├── CaresPharma-Insights.Infrastructure/ # Database Factories and Background Workers
│   └── CaresPharma-Insights.Api/            # Endpoints, DI Setup, and Swagger UI
├── Chat/
│   └── CaresPharma-Insights.UI/             # Blazor Wasm UI Frontend
└── Shared/
    └── CaresPharma-Insights.Shared/         # Shared DTOs
