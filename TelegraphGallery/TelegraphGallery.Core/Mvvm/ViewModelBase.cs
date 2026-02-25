using Prism.Mvvm;
using Prism.Navigation;

namespace TelegraphGallery.Core.Mvvm
{
    public abstract class ViewModelBase : BindableBase, IDestructible
    {
        protected abstract void DefineEvents();
        protected abstract void DefineCommands();

        protected void Initialize()
        {
            DefineCommands();
            DefineEvents();
        }

        public virtual void Destroy()
        {
        }
    }
}
