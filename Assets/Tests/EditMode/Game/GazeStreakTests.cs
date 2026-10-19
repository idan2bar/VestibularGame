using NUnit.Framework;

namespace VestibularGame.Game.Tests
{
    public class GazeStreakTests
    {
        [Test]
        public void NoLevelBeforeStep()
        {
            var s = new GazeStreak(5f, 6);
            Assert.AreEqual(StreakTransition.None, s.Step(true, 4.5f));
            Assert.AreEqual(0, s.Level);
            Assert.AreEqual(4.5f, s.StreakSeconds, 1e-4f);
        }

        [Test]
        public void LevelsUpAtExactStepBoundaries()
        {
            var s = new GazeStreak(5f, 6);
            Assert.AreEqual(StreakTransition.None, s.Step(true, 4.5f));
            Assert.AreEqual(StreakTransition.LevelUp, s.Step(true, 0.5f));
            Assert.AreEqual(1, s.Level);
            Assert.AreEqual(StreakTransition.None, s.Step(true, 4.5f));
            Assert.AreEqual(StreakTransition.LevelUp, s.Step(true, 0.5f));
            Assert.AreEqual(2, s.Level);
        }

        [Test]
        public void ManySmallStepsFireExactlyOneLevelUp()
        {
            var s = new GazeStreak(5f, 6);
            int levelUps = 0;
            // 5.5 s of looking at 60 fps.
            for (int i = 0; i < 330; i++)
                if (s.Step(true, 1f / 60f) == StreakTransition.LevelUp) levelUps++;
            Assert.AreEqual(1, levelUps);
            Assert.AreEqual(1, s.Level);
        }

        [Test]
        public void LargeDtJumpingTwoLevelsReturnsOneLevelUp()
        {
            var s = new GazeStreak(5f, 6);
            Assert.AreEqual(StreakTransition.LevelUp, s.Step(true, 10.5f));
            Assert.AreEqual(2, s.Level);
            Assert.AreEqual(StreakTransition.None, s.Step(true, 0.5f));
        }

        [Test]
        public void CappedAtMaxLevelWhileStreakKeepsGrowing()
        {
            var s = new GazeStreak(1f, 3);
            Assert.AreEqual(StreakTransition.LevelUp, s.Step(true, 3f));
            Assert.AreEqual(3, s.Level);
            Assert.AreEqual(StreakTransition.None, s.Step(true, 5f));
            Assert.AreEqual(3, s.Level);
            Assert.AreEqual(8f, s.StreakSeconds, 1e-4f);
        }

        [Test]
        public void StepProgressTracksPartialStepAndCap()
        {
            var s = new GazeStreak(5f, 2);
            Assert.AreEqual(0f, s.StepProgress, 1e-5f);
            s.Step(true, 2.5f);
            Assert.AreEqual(0.5f, s.StepProgress, 1e-4f);
            s.Step(true, 5f); // 7.5 s: level 1, halfway to level 2
            Assert.AreEqual(0.5f, s.StepProgress, 1e-4f);
            s.Step(true, 5f); // 12.5 s: capped
            Assert.AreEqual(2, s.Level);
            Assert.AreEqual(1f, s.StepProgress);
        }

        [Test]
        public void LookAwayResetsStreak()
        {
            var s = new GazeStreak(5f, 6);
            s.Step(true, 7f);
            Assert.AreEqual(StreakTransition.Reset, s.Step(false, 0.05f));
            Assert.AreEqual(0, s.Level);
            Assert.AreEqual(0f, s.StreakSeconds);
            Assert.AreEqual(0f, s.StepProgress);
        }

        [Test]
        public void LookAwayWithoutStreakReturnsNone()
        {
            var s = new GazeStreak(5f, 6);
            Assert.AreEqual(StreakTransition.None, s.Step(false, 0.05f));
            s.Step(true, 1f);
            Assert.AreEqual(StreakTransition.Reset, s.Step(false, 0.05f));
            Assert.AreEqual(StreakTransition.None, s.Step(false, 0.05f));
        }

        [Test]
        public void CanLevelUpAgainAfterReset()
        {
            var s = new GazeStreak(5f, 6);
            s.Step(true, 6f);
            s.Step(false, 0.05f);
            Assert.AreEqual(StreakTransition.LevelUp, s.Step(true, 5f));
            Assert.AreEqual(1, s.Level);
        }

        [Test]
        public void NegativeDtIsTreatedAsZero()
        {
            var s = new GazeStreak(5f, 6);
            s.Step(true, 1f);
            Assert.AreEqual(StreakTransition.None, s.Step(true, -3f));
            Assert.AreEqual(1f, s.StreakSeconds, 1e-5f);
            Assert.AreEqual(StreakTransition.Reset, s.Step(false, -1f));
        }

        [Test]
        public void ResetIsSilent()
        {
            var s = new GazeStreak(5f, 6);
            s.Step(true, 6f);
            s.Reset();
            Assert.AreEqual(0, s.Level);
            Assert.AreEqual(0f, s.StreakSeconds);
            Assert.AreEqual(StreakTransition.None, s.Step(false, 0.05f));
        }

        [Test]
        public void StepSecondsAndMaxLevelAreClamped()
        {
            var s = new GazeStreak(0f, 0);
            Assert.AreEqual(0.01f, s.StepSeconds, 1e-6f);
            Assert.AreEqual(1, s.MaxLevel);

            s.StepSeconds = -4f;
            s.MaxLevel = -2;
            Assert.AreEqual(0.01f, s.StepSeconds, 1e-6f);
            Assert.AreEqual(1, s.MaxLevel);

            s.StepSeconds = 2f;
            s.MaxLevel = 4;
            Assert.AreEqual(2f, s.StepSeconds);
            Assert.AreEqual(4, s.MaxLevel);
        }
    }
}
