using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Net;
using System.Collections.ObjectModel;
using System.Collections.Generic;

using System.Threading;
using System.Reflection;

using PegasusShared;

namespace HearthstoneBot
{
    public class AIBot
    {
        public enum Mode
        {
            TOURNAMENT_RANKED,
            TOURNAMENT_UNRANKED,
            PRACTICE_NORMAL,
            PRACTICE_EXPERT
        };

        public void setMode(Mode m)
        {
            Log.log("GameMode changed to : " + m.ToString());
            game_mode = m;
        }

        private volatile Mode game_mode = Mode.TOURNAMENT_RANKED;

        private API api = null;
        private readonly System.Random random = null;
        
        private DateTime delay_start = DateTime.Now;
        private long delay_length = 0;

        // Queued actions
        private List<Action> queuedActions = new List<Action>();

        // Keep track of the last mode
        private SceneMgr.Mode last_scene_mode = SceneMgr.Mode.STARTUP;

        public AIBot()
        {
            random = new System.Random();
            api = new API();
        }

        public void ReloadScripts()
        {
            api = new API();
        }

        // Delay the next Mainloop entry, by msec
        private void Delay(long msec)
        {
            delay_start = DateTime.Now;
            delay_length = msec;
        }

        // Do a single AI tick
        public void tick()
        {
            // Handle any required delays
            // Get current time
            DateTime current_time = DateTime.Now;
            // Figure the time passed, since delay was requested
            TimeSpan time_since_delay = current_time - delay_start;
            // If the time passed, is less than the required delay
            if (time_since_delay.TotalMilliseconds < delay_length)
            {
                // Simply return, for more time to pass
                return;
            }

            // Try to run the main loop
            try
            {
                // Only do this, if the bot is running
                if (Plugin.isRunning())
                {
                    update();
                }
            }
            catch(Exception e)
            {
                Log.error("Exception in AI: " + e.Message);
                Log.error(e.ToString());
                Delay(10000);
            }
        }

        // Get a random PRACTICE AI mission
        private int getRandomAIMissionId(bool expert)
        {
            List<int> AI_Selected = new List<int>();

            // Get mission IDs from SCENARIO.XML
            AdventureModeId mode = expert ? AdventureModeId.EXPERT : AdventureModeId.NORMAL;
            foreach (DbfRecord current in GameDbf.Scenario.GetRecords())
            {
                if (current.GetInt("ADVENTURE_ID") == (int)AdventureId.PRACTICE)
                {
                    if (current.GetInt("MODE_ID") == (int)mode)
                    {
                        AI_Selected.Add(current.GetInt("ID"));
                    }
                }
            }

            // Pick a random index
            int index = random.Next(AI_Selected.Count);
            // Return the corresponding ID
            return AI_Selected[index];
        }

        // Return whether Mulligan was done
        private void mulligan()
        {
            // Get hand cards
            List<Card> cards = API.getOurPlayer().GetHandZone().GetCards().ToList<Card>();
            // Ask the AI scripting system, to figure which cards to replace
            List<Card> replace = api.mulligan(cards);

            // Toggle them as replaced
            MulliganManager mm = MulliganManager.Get();
            foreach (Card current in replace)
            {
                Log.log(current.ToString());
                mm.ToggleHoldState(current);
            }

            // Report progress
            Log.log("Mulligan Ended : " + replace.Count + " cards changed");
        }

        // Welcome / login screen
        private void login_mode()
        {
            // If there are any welcome quests on the screen
            if (WelcomeQuests.Get() != null)
            {
                Log.say("Entering to main menu");
                // Emulate a next click
                WelcomeQuests.Get().m_clickCatcher.TriggerRelease();
            }

            // Delay after clicking quests
            Delay(5000);
        }

        bool just_joined = false;

        // Found at: DeckPickerTrayDisplay search for RankedMatch
        private void tournament_mode(bool ranked)
        {
            if (just_joined)
                return;

            // Don't do this, if we're currently in a game, or matching a game
            // TODO: Change to an assertion
            if (SceneMgr.Get().IsInGame() || GameMgr.Get().IsFindingGame())
            {
                return;
            }

            // If we're not set to the right mode, now is the time to do so
            // Note; This does not update the GUI, only the internal state
            bool is_ranked = Options.Get().GetBool(Option.IN_RANKED_PLAY_MODE);
            if(is_ranked != ranked)
            {
                Options.Get().SetBool(Option.IN_RANKED_PLAY_MODE, ranked);
                Delay(3000);
                return;
            }

            // Get the ID of the current Deck
            long selectedDeckID = DeckPickerTrayDisplay.Get().GetSelectedDeckID();
            // We want to play vs other players
            int mission = (int)MissionId.MULTIPLAYER_1v1;
            // Ranked or unranked?
            GameType mode = ranked ? GameType.GT_RANKED : GameType.GT_UNRANKED;
            // Find the game
            Log.log("Joining game in tournament mode, ranked = " + ranked);
            DeckPickerTrayDisplay.Get().ShowMatchingPopup();
            GameMgr.Get().FindGame(mode, mission, selectedDeckID);

            just_joined = true;
        }

        bool deck_initialized = false;

        // Play against AI
        // Found at: PracticePickerTrayDisplay search for StartGame
        private void practice_mode(bool expert)
        {
            if (just_joined)
                return;

            // Don't do this, if we're currently in a game
            // TODO: Change to an assertion
            if (SceneMgr.Get().IsInGame())
            {
                return;
            }

            if (! deck_initialized) {
                Log.log("Changing adventureconfig...");
                AdventureConfig.Get().SetSelectedAdventureMode(AdventureId.PRACTICE, expert ? AdventureModeId.EXPERT : AdventureModeId.NORMAL);
                AdventureConfig.Get().ChangeSubScene(AdventureSubScenes.MissionDeckPicker);

                deck_initialized = true;
                Delay(5000);
            } else {
                // Get the ID of the current Deck
                Log.log("Getting Deck id");
                long selectedDeckID = DeckPickerTrayDisplay.Get().GetSelectedDeckID();
                if (selectedDeckID == 0) {
                    Log.error("Invalid Deck ID 0!");
                    return;
                }

                // Get a random mission, of selected difficulty
                int mission = getRandomAIMissionId(expert);

                // Start up the game
                Log.log("Starting game in practice mode, expert = " + expert + ", mission = " + mission + ", deck = " + selectedDeckID);
                DeckPickerTrayDisplay.Get().GetLoadingPopup().Show();
                GameMgr.Get().FindGame(GameType.GT_VS_AI, mission, selectedDeckID);

                just_joined = true;
                deck_initialized = false;
                Delay(5000);
            }
        }

        // Called when a game is ended
        private void game_over()
        {
            // Write why the game ended
            if (API.getEnemyPlayer().GetHero().GetRemainingHP() <= 0)
            {
                Log.say("Victory!");
            }
            else if (API.getOurPlayer().GetHero().GetRemainingHP() <= 0)
            {
                Log.say("Defeat...");
            }
            else
            {
                Log.say("Draw..?");
            }

            // Click through end screen info (rewards, and such)
            if (EndGameScreen.Get() != null)
            {
                EndGameScreen.Get().m_hitbox.TriggerRelease();

                //EndGameScreen.Get().ContinueEvents();
            }

            // Delay 10 seconds after this method
            Delay(10000);
        }

        private void end_turn()
        {
            InputManager.Get().DoEndTurnButton();
            Delay(10000);
        }

        // Called to invoke AI
        private void run_ai()
        {
            // Temporarily disable reticle so mouse doesn't have to stay in window
            TargetReticleManager trm = TargetReticleManager.Get();
            PrivateHacker.set_TargetReticleManager_s_instance(null);

            try
            {
                // Perform queued actions first
                if(queuedActions.Count > 0)
                {
                    // Dequeue first execution and perform it
                    Action action = queuedActions[0];
                    queuedActions.RemoveAt(0);
                    int delay = api.PerformAction(action);

                    // Delay between each action
                    Delay(delay);
                    return;
                }

                // Get hand cards
                var cards = API.getOurPlayer().GetHandZone().GetCards().ToList<Card>();

                // Get initial actions to perform
                var actions = api.turn(cards);

                // Queue up these actions
                queuedActions.AddRange(actions);

                if (queuedActions.Count == 0)
                {
                    // Done with turn actions
                    Log.log("Ending turn");
                    end_turn();
                }
            }
            catch (Exception e)
            {
                Log.error("Exception in run_ai: " + e.Message);
                Log.error(e.ToString());
            }

            // Re-enable TargetReticleManager
            PrivateHacker.set_TargetReticleManager_s_instance(trm);
        }

        // Used to manage delays for some phases
        private bool was_my_turn = false;

        // Keep track of if we ended mulligan
        private enum MulliganState { BEGIN, DO_END, DONE };
        private MulliganState mulligan_state = MulliganState.BEGIN;

        private void gameplay_mode()
        {
            GameState gs = GameState.Get();

            // If we're in mulligan
            if (gs.IsMulliganPhase())
            {
                if (mulligan_state == MulliganState.BEGIN)
                {
                    if(gs.IsMulliganManagerActive() && PrivateHacker.get_m_UIbuttons() != null)
                    {
                        mulligan();
                        mulligan_state = MulliganState.DO_END;
                        Delay(2000);
                    }
                }
                else if (mulligan_state == MulliganState.DO_END)
                {
                    MulliganManager.Get().AutomaticContinueMulligan();
                    mulligan_state = MulliganState.DONE;
                }
                return;
            }
            // If the game is over
            else if (gs.IsGameOver())
            {
                game_over();
            }
            // If it's our turn
            else if (gs.IsLocalPlayerTurn())
            {
                // If it was not our turn last tick
                if (!was_my_turn)
                {
                    // Wait extra time for turn to start
                    was_my_turn = true;
                    Delay(5000);
                    return;
                }

                run_ai();
            }
            else
            {
                was_my_turn = false;
            }

            // Reset variables
            mulligan_state = MulliganState.BEGIN;
        }

        // Run a single AI tick
        private void update()
        {
            // Avoid InactivePlayerKicker
            PrivateHacker.set_m_activityDetected(true);

            // Get current scene mode
            SceneMgr.Mode scene_mode = SceneMgr.Get().GetMode();

            // If scene changes let's wait a few seconds
            if (scene_mode != last_scene_mode)
            {
                last_scene_mode = scene_mode;
                Delay(5000);
                return;
            }

            // Switch upon the mode
            switch (scene_mode)
            {
                // Unsupported modes
                case SceneMgr.Mode.STARTUP:
                case SceneMgr.Mode.COLLECTIONMANAGER:
                case SceneMgr.Mode.PACKOPENING:
                case SceneMgr.Mode.FRIENDLY:
                case SceneMgr.Mode.DRAFT:
                case SceneMgr.Mode.CREDITS:
                    // Enter MainMenu
                    SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
                    break;

                // Errors, nothing to do
                case SceneMgr.Mode.INVALID:
                case SceneMgr.Mode.FATAL_ERROR:
                case SceneMgr.Mode.RESET:
                    Log.say("Fatal Error, in AI.tick()", true);
                    Log.say("Force closing game!", true);
                    Plugin.destroy();
                    // Kill it the bad way
                    Environment.FailFast(null);
                    //Plugin.setRunning(false);
                    break;

                // Login screen
                case SceneMgr.Mode.LOGIN:
                    // Click through quests
                    login_mode();
                    break;

                // Main Menu
                case SceneMgr.Mode.HUB:
                    switch(game_mode)
                    {
                        case Mode.PRACTICE_NORMAL:
                        case Mode.PRACTICE_EXPERT:
                            // Enter PRACTICE Mode
                            SceneMgr.Get().SetNextMode(SceneMgr.Mode.ADVENTURE);
                            break;

                        case Mode.TOURNAMENT_RANKED:
                        case Mode.TOURNAMENT_UNRANKED:
                            // Enter Turnament Mode
                            SceneMgr.Get().SetNextMode(SceneMgr.Mode.TOURNAMENT);
                            Tournament.Get().NotifyOfBoxTransitionStart();
                            break;

                        default:
                           throw new Exception("Unknown Game Mode!");
                    }
                    break;

                // In game
                case SceneMgr.Mode.GAMEPLAY:

                    // Handle Gamplay
                    gameplay_mode();
                    just_joined = false;
                    break; 

                // In PRACTICE Sub Menu
                case SceneMgr.Mode.ADVENTURE:
                    bool expert = false;
                    switch(game_mode)
                    {
                        case Mode.PRACTICE_NORMAL:
                            expert = false;
                            break;

                        case Mode.PRACTICE_EXPERT:
                            expert = true;
                            break;

                        case Mode.TOURNAMENT_RANKED:
                        case Mode.TOURNAMENT_UNRANKED:
                            // Leave to the Hub
                            Log.say("Inside wrong sub-menu!");
                            SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
                            return;

                        default:
                            throw new Exception("Unknown Game Mode!");
                    }

                    // Play against AI
                    practice_mode(expert);
                    break;

                // In Play Sub Menu
                case SceneMgr.Mode.TOURNAMENT:
                    bool ranked = false;
                    switch(game_mode)
                    {
                        case Mode.PRACTICE_NORMAL:
                        case Mode.PRACTICE_EXPERT:
                            // Leave to the Hub
                            Log.say("Inside wrong sub-menu!");
                            SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
                            return;

                        case Mode.TOURNAMENT_RANKED:
                            ranked = true;
                            break;

                        case Mode.TOURNAMENT_UNRANKED:
                            ranked = false;
                            break;

                        default:
                            throw new Exception("Unknown Game Mode!");
                    }

                    // Play against humans (or bots)
                    tournament_mode(ranked);
                    break;

                default:
                    Log.say("Unknown SceneMgr State!", true);
                    break;
            }
        }
    }
}
