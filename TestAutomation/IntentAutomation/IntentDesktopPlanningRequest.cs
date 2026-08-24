using System;
using System.Collections.Generic;

namespace IntentAutomation
{
    /// <summary>
    /// Represents an intent planning request tailored specifically for desktop automation scenarios,
    /// omitting web-specific concepts like <c>TargetUrl</c>.
    /// </summary>
    public sealed class IntentDesktopPlanningRequest
    {
        /// <summary>
        /// Optional human-readable name for the desktop test scenario.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The business or testing goal to achieve in the desktop application (e.g. "Create a customer record").
        /// </summary>
        public string Goal { get; set; } = "";

        /// <summary>
        /// Key-value test data pairs used to populate desktop form controls during planning.
        /// </summary>
        public IDictionary<string, string> TestData { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Optional path to the compiled application executable under test.
        /// </summary>
        public string ApplicationExecutablePath { get; set; } = "";

        /// <summary>
        /// Converts this desktop planning request into a standard <see cref="IntentPlanningRequest"/> with an empty <c>TargetUrl</c>.
        /// </summary>
        public IntentPlanningRequest ToPlanningRequest()
        {
            return new IntentPlanningRequest
            {
                Name = Name,
                Goal = Goal,
                TargetUrl = "",
                TestData = TestData != null
                    ? new Dictionary<string, string>(TestData)
                    : new Dictionary<string, string>(),
            };
        }

        /// <summary>
        /// Implicitly converts an <see cref="IntentDesktopPlanningRequest"/> to an <see cref="IntentPlanningRequest"/>.
        /// </summary>
        public static implicit operator IntentPlanningRequest(IntentDesktopPlanningRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return request.ToPlanningRequest();
        }
    }
}
