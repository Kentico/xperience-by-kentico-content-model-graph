using DancingGoat.Models;

namespace DancingGoat.Widgets
{
    /// <summary>
    /// View model for Story section widget.
    /// </summary>
    public class StorySectionWidgetViewModel
    {
        /// <summary>
        /// Story title.
        /// </summary>
        public string Title { get; set; }


        /// <summary>
        /// Story text.
        /// </summary>
        public string Text { get; set; }


        /// <summary>
        /// Gets ViewModel for <paramref name="storySection"/>.
        /// </summary>
        public static StorySectionWidgetViewModel GetViewModel(StorySection storySection)
        {
            if (storySection == null)
            {
                return null;
            }

            return new StorySectionWidgetViewModel
            {
                Title = storySection.StorySectionTitle,
                Text = storySection.StorySectionText
            };
        }
    }
}
