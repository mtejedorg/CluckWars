# Unity 6000.3.14f1 + UI Toolkit + MCP Architecture Guide

## System Context
You are managing a Unity 6000.3.14f1 project using an open-source MCP server (`IvanMurzak/Unity-MCP`) to handle Editor communication. This bypasses the official Unity MCP, eliminating licensing costs.

---

## 1. The UI Architecture (Strict Rules)

Do NOT generate `uGUI` Canvas-based UI or serialized YAML Prefabs for interfaces. You must exclusively use **UI Toolkit**.

### Structure
Write `.uxml` files using UI Toolkit Flexbox DOM structure.

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
    <ui:VisualElement class="menu-container">
        <ui:Label text="Main Menu" class="menu-container__header" />
        <ui:Button text="Start Game" name="start-btn" class="menu-container__button" />
    </ui:VisualElement>
</ui:UXML>
```

### Styling
Write `.uss` files using standard class-based targeting:

```uss
.menu-container {
    flex-direction: column;
    justify-content: center;
    align-items: center;
    width: 100%;
    height: 100%;
    background-color: rgb(20, 20, 20);
}

.menu-container__header {
    font-size: 32px;
    color: rgb(255, 200, 0);
    margin-bottom: 30px;
}

.menu-container__button {
    width: 200px;
    height: 50px;
    margin: 10px;
}
```

### Logic
Write C# `MonoBehaviour` controllers that require a `UIDocument` component. Use the `Q<T>("element-name")` query API:

```csharp
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuController : MonoBehaviour
{
    private void Start()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        var startBtn = root.Q<Button>("start-btn");
        startBtn.clicked += OnStartClicked;
    }

    private void OnStartClicked()
    {
        // Handle start game logic
    }
}
```

---

## 2. Execution Workflow

1. **Environment Sync:** Use MCP tools to read the current Editor state and project hierarchy.
2. **UI Generation:** When translating visual designs, wait for the user to provide HTML/CSS exports from Figma Dev Mode. Translate these directly into UXML/USS files and write them to `Assets/UI/`.
3. **Controller Binding:** Generate the corresponding C# scripts and use MCP tools to attach them to the relevant Scene objects containing the `UIDocument`.

---

## 3. Setup & Installation

### Prerequisites
- Node.js (for OpenUPM CLI)
- Unity 6000.3.14f1 project open

### Installation Steps

1. **Install OpenUPM CLI** (skip if already installed):
   ```bash
   npm install -g openupm-cli
   ```

2. **Install the Unity Plugin:**
   Navigate to your project root and run:
   ```bash
   openupm add com.ivanmurzak.unity.mcp
   ```

3. **Register with Claude Code:**
   Run this in your project directory to bind Claude Code to the MCP server:
   ```bash
   claude mcp add --transport stdio ivanmurzak-unity-mcp uvx unity-mcp
   ```

4. **Verify Connection:**
   Start Claude Code and test with:
   ```
   "Ping the Unity Editor and list the current scene hierarchy."
   ```

---

## 4. MCP Tool Reference

Available tools for Editor interaction:

- **Scene Queries:** Inspect scene hierarchy, find GameObjects, query components
- **Asset Inspection:** Read asset metadata, query prefabs, locate scripts
- **Compilation:** Check compiler errors, validate code
- **Play Mode Control:** Start/stop play mode, query runtime state

See the open-source repo for full tool documentation: https://github.com/IvanMurzak/Unity-MCP

---

## 5. Asset Structure

- **UI Layouts:** `Assets/UI/` — UXML files organized by screen/feature
- **UI Styles:** `Assets/UI/Styles/` — USS files (global + component-specific)
- **UI Scripts:** `Assets/Scripts/UI/` — MonoBehaviour controllers
- **Prefabs:** `Assets/Prefabs/` — Non-UI prefabs only (UI is purely toolkit-based)

---

## 6. Common Patterns

### Binding a UI Controller to a Scene
1. Create `Assets/UI/MyScreen.uxml` and `Assets/UI/Styles/MyScreen.uss`
2. Create `Assets/Scripts/UI/MyScreenController.cs`
3. In the Scene, create an empty GameObject named "MyScreenUI"
4. Add a `UIDocument` component and assign the `.uxml` file
5. Add your `MyScreenController` script to the same GameObject

### Querying Elements
```csharp
var root = GetComponent<UIDocument>().rootVisualElement;
var button = root.Q<Button>("my-button");
var label = root.Q<Label>("my-label");
```

### Adding Event Listeners
```csharp
button.clicked += () => Debug.Log("Button clicked!");
label.RegisterCallback<PointerOverEvent>(evt => Debug.Log("Hovered"));
```

---

## 7. Troubleshooting

| Issue | Solution |
|-------|----------|
| MCP connection revoked | Restart the Editor and re-register: `claude mcp add --transport stdio ivanmurzak-unity-mcp uvx unity-mcp` |
| UXML file not found | Verify path is relative to project root and file exists in `Assets/UI/` |
| Script not binding to GameObject | Ensure the `UIDocument` component is present and the UXML file is assigned |
| Styling not applied | Check USS selectors match element names/classes in UXML |

