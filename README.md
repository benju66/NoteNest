# 📝 NoteNest

**NoteNest** is a lightweight, modular note-taking desktop app built with **C# and WPF**, inspired by the simplicity of Notepad but enhanced with organizational tools like a collapsible category tree and tabbed editing.

Designed to function as a **plugin-style panel** for larger applications or as a **standalone desktop tool**, NoteNest aims to provide fast, structured note access without the bulk of tools like OneNote.

---

## 🚀 Features

- 🌲 Left panel with tree view for categories and notes
- 🗂 Notes organized into user-defined folders
- 📄 Multiple notes open in tabs (like Notepad++)
- 📁 Hybrid storage system using real `.txt` files + JSON index
- 📌 Pinned categories
- 🔖 Future: Tags, versioning, and integration with custom file browsers

---

## 🧱 Architecture

| Layer | Purpose |
|-------|---------|
| `NoteNest.Core` | Models and plugin interfaces |
| `NoteNest.UI` | WPF user interface and controls |
| `NoteNest.Metadata` | Stores `categories.json` and sample data |
| `NoteNest.Tests` | Unit tests (coming soon) |

---

## 🧰 Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Windows 10/11
- Visual Studio Code or Visual Studio (for development)

---

## ⚙️ Run the App Locally

```bash
git clone https://github.com/benju66/NoteNest.git
cd NoteNest
dotnet build
dotnet run --project NoteNest.UI
