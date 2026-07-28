namespace CVPlus.Mac.Models;
public sealed record ServerState(string Ip, int Port, string SessionCode, string Mode, bool HeaderManagementAllowed);
