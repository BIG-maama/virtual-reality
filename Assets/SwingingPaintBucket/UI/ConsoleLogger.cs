using UnityEngine;
using System.IO;
using System.Text;

public class ConsoleLogger : MonoBehaviour
{
    private StringBuilder _log = new StringBuilder();
    private string _filePath;

    private void Awake()
    {
        _filePath = Path.Combine(Application.dataPath, "../ConsoleLog.txt");
        Application.logMessageReceived += HandleLog;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        _log.AppendLine($"[{type}] {logString}");
        if (type == LogType.Error || type == LogType.Exception)
            _log.AppendLine(stackTrace);
    }

    private void OnApplicationQuit() => SaveLog();
    private void OnDestroy() => SaveLog();

    private void SaveLog()
    {
        File.WriteAllText(_filePath, _log.ToString());
        Debug.Log($"[ConsoleLogger] Saved to {_filePath}");
    }
}