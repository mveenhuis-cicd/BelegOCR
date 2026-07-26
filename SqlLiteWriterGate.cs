namespace BelegOCR
{
    /// <summary>
    /// Gate with Semaphore for writer problem
    /// </summary>
    public class SqlLiteWriterGate
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public async Task<T> WriteAsync<T>(Func<Task<T>> action)
        {
            await _gate.WaitAsync();
            try
            {
                return await action();
            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task WriteAsync<T>(Func<Task> action)
        {
            await _gate.WaitAsync();
            try
            {
                await action();
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}


