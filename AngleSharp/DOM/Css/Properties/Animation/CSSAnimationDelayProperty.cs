﻿namespace AngleSharp.DOM.Css
{
    using AngleSharp.Css;
    using AngleSharp.Extensions;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// More information available at:
    /// https://developer.mozilla.org/en-US/docs/CSS/animation-delay
    /// </summary>
    sealed class CSSAnimationDelayProperty : CSSProperty, ICssAnimationDelayProperty
    {
        #region Fields

        readonly List<Time> _times;

        #endregion

        #region ctor

        internal CSSAnimationDelayProperty(CSSStyleDeclaration rule)
            : base(PropertyNames.AnimationDelay, rule)
        {
            _times = new List<Time>();
            Reset();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the delays for the animations.
        /// </summary>
        public IEnumerable<Time> Delays
        {
            get { return _times; }
        }

        #endregion

        #region Methods

        public void SetDelays(IEnumerable<Time> times)
        {
            _times.Clear();
            _times.AddRange(times);
        }

        internal override void Reset()
        {
            _times.Clear();
            _times.Add(Time.Zero);
        }

        /// <summary>
        /// Determines if the given value represents a valid state of this property.
        /// </summary>
        /// <param name="value">The state that should be used.</param>
        /// <returns>True if the state is valid, otherwise false.</returns>
        protected override Boolean IsValid(CSSValue value)
        {
            return this.TakeList(this.WithTime()).TryConvert(value, SetDelays);
        }

        #endregion
    }
}
