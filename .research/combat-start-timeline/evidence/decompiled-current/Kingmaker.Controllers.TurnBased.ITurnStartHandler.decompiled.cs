using Kingmaker.EntitySystem.Interfaces;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.PubSubSystem.Core.Interfaces;

namespace Kingmaker.Controllers.TurnBased;

public interface ITurnStartHandler : ISubscriber<IMechanicEntity>, ISubscriber
{
	void HandleUnitStartTurn(bool isTurnBased);
}
