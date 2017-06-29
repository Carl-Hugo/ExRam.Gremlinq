using System.Collections.Immutable;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;

namespace ExRam.Gremlinq.Tests
{
    public class GremlinQueryProviderTest
    {
        [Fact]
        public void WithSubgraphStrategyTest()
        {
            var queryProviderMock = new Mock<IGremlinQueryProvider>();
            var subgraphStrategyProvider = queryProviderMock.Object.WithSubgraphStrategy(_ => _, _ => _);

            subgraphStrategyProvider
                .Execute(GremlinQuery.Create("g").Cast<Unit>());

            queryProviderMock.Verify(x => x.Execute<Unit>(It.Is<IGremlinQuery<Unit>>(query => (query.Steps[0] is TerminalGremlinStep) && ((TerminalGremlinStep)query.Steps[0]).Name == "withStrategies" && ((TerminalGremlinStep)query.Steps[0]).Parameters.Count == 1)));
        }

        [Fact]
        public async Task Scalar()
        {
            var queryProviderMock = new Mock<IGremlinQueryProvider>();
            queryProviderMock
                .Setup(x => x.Execute(It.IsAny<IGremlinQuery<string>>()))
                .Returns(AsyncEnumerable.Return("36"));

            var value = await queryProviderMock.Object
                .WithModel(GraphModel.FromAssembly(Assembly.GetExecutingAssembly(), typeof(Vertex), typeof(Edge), GraphElementNamingStrategy.Simple))
                .WithJsonSupport()
                .Execute(Mock.Of<IGremlinQuery<int>>(x => x.StepLabelMappings == ImmutableDictionary<string, StepLabel>.Empty))
                .First();

            value.Should().Be(36);
        }
    }
}