namespace TransactionMatching.Application.Matching;

/// <summary>El archivo no tiene el formato esperado; el mensaje se muestra tal cual al usuario.</summary>
public class MatchingFileException(string message) : Exception(message);
