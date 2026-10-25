// Starts `lucode --lsp` and lets VS Code show its errors. Needs `lucode` on the PATH
// (or set "lucode.path" in the settings).
const vscode = require("vscode");
const { LanguageClient } = require("vscode-languageclient/node");

let client;

function activate() {
  const command = vscode.workspace.getConfiguration("lucode").get("path") || "lucode";
  client = new LanguageClient(
    "lucode",
    "Lucode",
    { command, args: ["--lsp"] },
    { documentSelector: [{ language: "lucode" }] }
  );
  client.start();
}

function deactivate() {
  return client ? client.stop() : undefined;
}

module.exports = { activate, deactivate };
