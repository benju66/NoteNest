# 🔬 Absolute Final Diagnostic Test

**Added:** Serilog logging to NullableGuidTypeHandler.Parse()  
**This will show:** Exactly what Dapper is passing to the type handler

---

## 📋 **RUN THIS SEQUENCE:**

```powershell
# 1. Force close
taskkill /F /IM NoteNest.UI.exe

# 2. Rebuild  
cd C:\NoteNest
dotnet clean
dotnet build

# 3. Fresh database
Remove-Item "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin\*.*" -Force

# 4. Run
.\Launch-NoteNest.bat

# 5. Add category, create todo, close, reopen

# 6. Get type handler logs
Get-Content "$env:LOCALAPPDATA\NoteNest\Logs\notenest-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "GuidTypeHandler" | Select-Object -Last 30
```

---

## 🔍 **WHAT THE LOGS WILL REVEAL:**

**Scenario A: Type handler called with string**
```
[GuidTypeHandler] Parse: '54256f7f' → 54256f7f-... ✅
```
- Type handler working
- Bug is elsewhere

**Scenario B: Type handler called with null**
```
[GuidTypeHandler] Parse: value=null/DBNull → returning null
```
- Database returning NULL
- Or Dapper not reading column

**Scenario C: NO type handler logs**
- Type handler NOT being invoked
- Dapper not using it
- Registration issue

---

**Share the "GuidTypeHandler" log output and I'll know exactly what's wrong!**

