# WPF Screen Capture - Development Guide

## Build and Run Commands
##We can't use `dotnet build` and `dotnet run` commands for this project because claude is run from WSL and it's a window program.

## Code Style Guidelines
- **Naming Conventions**:
  - Use PascalCase for classes, public methods, and properties
  - Use camelCase for private fields and local variables
  - Prefix private fields with underscore (_)
  - Use "Instance" for singletons
  - Use functions like SetVariableName and GetVariableName for properties
  
- **Layout**:
  - 4-space indentation (no tabs)
  - Braces on new lines (Allman style)
  - Group and order imports by namespace (System namespaces first)
  
- **Design Patterns**:
  - Place P/Invoke declarations at top of class
  - For UI, use Get/Sets that directly read/write to the GUI element instead of private variables when possible
  - Separate visual elements with blank lines for readability
  - Try to create things like WindowOCRManager or ConfigManager to group related functions (those were examples)


- **Error Handling**:
   - Try to avoid Try/catch blocks, they look bad.  Check for null and stuff like that and write errors to the console.