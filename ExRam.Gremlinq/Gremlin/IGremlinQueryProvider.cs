using System.Collections.Generic;

namespace ExRam.Gremlinq
{
    public interface IGremlinQueryProvider
    {
        IGremlinQuery CreateQuery();

        IAsyncEnumerable<T> Execute<T>(IGremlinQuery<T> query);

        IGraphModel Model { get; }
    }
}