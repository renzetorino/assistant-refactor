# 📱 BuiswAIz (App Workspace)

 This project is a distributed, multi-stack management system that combines a React frontend with dual-backend processing for business logic and AI orchestration.

## 📂 Project Structure

The workspace is divided into two main environments to separate business operations from AI processing:

* **/BuiswAIz:** The React.js (Vite) frontend application.
* **/C-sharp:** The .NET 8 AI backend, responsible for RAG, Insight Scanning, and the Gemini integration.
* **/Render-Backend (External):** The JavaScript/Node.js service handling core CRUD operations (Inventory, POS, Expenses).

---

## 🛠️ Technical Stack & Deployment

| Component | Technology | Hosted On |
| --- | --- | --- |
| **Frontend** | React + Vite | **Vercel** |
| **Business API** | JavaScript (Node.js) | **Render** |
| **AI Engine** | .NET 8 (C#) | **Hugging Face Spaces** |
| **Database** | PostgreSQL | **Supabase** |
