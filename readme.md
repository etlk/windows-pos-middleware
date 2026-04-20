# 🧰 C# Project Setup & Run Guide

## 💻 Prerequisites

### 1. Install .NET SDK
Download and install:
https://dotnet.microsoft.com/download

Verify installation:
dotnet --version

---

### 2. Install Visual Studio (Recommended)

Download:
https://visualstudio.microsoft.com/

During installation, select:
- .NET Desktop Development

---

## ▶️ How to Run the Project

### Option 1: Using Visual Studio

1. Open Visual Studio  
2. Click "Open a project or solution"  
3. Select the `.sln` file  
4. Build the project:
   Ctrl + Shift + B  
5. Run the project:
   Ctrl + F5  

---

### Option 2: Using Command Line

1. Open terminal or command prompt  
2. Navigate to project folder:
   cd project-name  

3. Restore dependencies:
   dotnet restore  

4. Build project:
   dotnet build  

5. Run project:
   dotnet run  

---

## 🐞 Troubleshooting

- Ensure .NET SDK is installed  
- Run `dotnet restore` if packages are missing  
- Check correct project folder before running  
