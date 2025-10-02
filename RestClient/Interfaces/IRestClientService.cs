namespace NameProject.RestClient.Interfaces;

public interface IRestClientService
{
    Task<HttpResponseMessage> Post(string service, string path, object payload);
    Task<HttpResponseMessage> Post(string service, string path, string payload);
    Task<T> Post<T>(string service, string path, object payload);
    Task<T> Post<T>(string service, string path, string payload);
    Task<HttpResponseMessage> Get(string service, string path);
    Task<T> Get<T>(string service, string path);
    Task<HttpResponseMessage> Put(string service, string path, object payload);
    Task<T> Put<T>(string service, string path, object payload);
    Task<HttpResponseMessage> Delete(string service, string path);
    Task<T> Delete<T>(string service, string path);
}
