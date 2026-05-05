namespace NameProject.RestClient.Interfaces;

public interface IRestClientSerializer
{
    string Serialize<T>(T value);
    T Deserialize<T>(string content);
}
