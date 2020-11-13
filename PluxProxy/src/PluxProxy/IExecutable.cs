using System.Threading.Tasks;

public interface IExecutable
{
    Task<int> Start();
    void Stop();
}
