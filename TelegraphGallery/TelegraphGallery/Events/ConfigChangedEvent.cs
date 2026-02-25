using Prism.Events;
using TelegraphGallery.Models;

namespace TelegraphGallery.Events
{
    public class ConfigChangedEvent : PubSubEvent<AppConfig>;
}
