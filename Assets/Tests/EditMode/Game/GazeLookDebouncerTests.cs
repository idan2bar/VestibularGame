using NUnit.Framework;

namespace VestibularGame.Game.Tests
{
    public class GazeLookDebouncerTests
    {
        const float Dt = 0.05f;

        // Steps n times and returns the first non-None transition (or None).
        static LookTransition Run(GazeLookDebouncer d, bool raw, int n)
        {
            LookTransition first = LookTransition.None;
            for (int i = 0; i < n; i++)
            {
                var t = d.Step(raw, Dt);
                if (first == LookTransition.None) first = t;
            }
            return first;
        }

        [Test]
        public void StartsOnlyAfterDelay()
        {
            var d = new GazeLookDebouncer(0.2f, 0.3f);
            Assert.AreEqual(LookTransition.None, Run(d, true, 3));
            Assert.IsFalse(d.IsLooking);
            Assert.AreEqual(LookTransition.Started, Run(d, true, 2));
            Assert.IsTrue(d.IsLooking);
        }

        [Test]
        public void ShortFlickerDoesNotStart()
        {
            var d = new GazeLookDebouncer(0.2f, 0.3f);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(LookTransition.None, Run(d, true, 3));
                Assert.AreEqual(LookTransition.None, Run(d, false, 1));
            }
            Assert.IsFalse(d.IsLooking);
        }

        [Test]
        public void StopsOnlyAfterDelay()
        {
            var d = new GazeLookDebouncer(0f, 0.3f);
            d.Step(true, Dt);
            Assert.IsTrue(d.IsLooking);
            Assert.AreEqual(LookTransition.None, Run(d, false, 5));
            Assert.IsTrue(d.IsLooking);
            Assert.AreEqual(LookTransition.Stopped, Run(d, false, 2));
            Assert.IsFalse(d.IsLooking);
        }

        [Test]
        public void ShortDropoutDoesNotStop()
        {
            var d = new GazeLookDebouncer(0f, 0.3f);
            d.Step(true, Dt);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(LookTransition.None, Run(d, false, 4));
                Assert.AreEqual(LookTransition.None, Run(d, true, 1));
            }
            Assert.IsTrue(d.IsLooking);
        }

        [Test]
        public void ZeroDelaysChangeOnThatStep()
        {
            var d = new GazeLookDebouncer(0f, 0f);
            Assert.AreEqual(LookTransition.Started, d.Step(true, Dt));
            Assert.AreEqual(LookTransition.Stopped, d.Step(false, Dt));
            Assert.AreEqual(LookTransition.Started, d.Step(true, 0f));
            Assert.AreEqual(LookTransition.Stopped, d.Step(false, 0f));
        }

        [Test]
        public void ForceStopStopsAndResets()
        {
            var d = new GazeLookDebouncer(0f, 5f);
            Assert.AreEqual(LookTransition.None, d.ForceStop());
            d.Step(true, Dt);
            Run(d, true, 4);
            Assert.AreEqual(LookTransition.Stopped, d.ForceStop());
            Assert.IsFalse(d.IsLooking);
            Assert.AreEqual(0f, d.LookingDuration);
            Assert.AreEqual(LookTransition.None, d.ForceStop());
        }

        [Test]
        public void LookingDurationAccumulatesAndResets()
        {
            var d = new GazeLookDebouncer(0f, 0.3f);
            Assert.AreEqual(0f, d.LookingDuration);
            d.Step(true, Dt);
            Assert.AreEqual(0f, d.LookingDuration, 1e-5f);
            Run(d, true, 10);
            Assert.AreEqual(0.5f, d.LookingDuration, 1e-4f);
            d.Reset();
            Assert.AreEqual(0f, d.LookingDuration);
            Assert.IsFalse(d.IsLooking);
        }

        [Test]
        public void ChangingDelaysTakesEffect()
        {
            var d = new GazeLookDebouncer(1f, 1f) { StartDelay = 0.1f, StopDelay = 0.1f };
            Assert.AreEqual(LookTransition.Started, Run(d, true, 3));
            Assert.AreEqual(LookTransition.Stopped, Run(d, false, 3));
        }
    }
}
