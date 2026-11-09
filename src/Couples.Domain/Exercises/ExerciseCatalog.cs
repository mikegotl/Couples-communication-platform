namespace Couples.Domain.Exercises;

/// <summary>
/// The exercises the product offers. All wording here is original to this project and written
/// for clinician review; none of it is a diagnostic tool, an assessment or a copy of a published
/// therapy exercise. To change an exercise, add a new version rather than editing a published one
/// (a test fails if a published version's content hash changes).
/// </summary>
public static class ExerciseCatalog
{
    public static readonly ExerciseTemplate ReflectiveListening = new(
        "reflective-listening",
        1,
        "Hear it back",
        "Practise putting what your partner said into your own words before you reply, so they feel heard and you check you understood.",
        10,
        [
            new("intro", ExerciseStepKind.Info, "Why hear it back",
                "When a conversation is tense, we often reply to what we expected to hear rather than what was said. "
                + "Saying it back first slows things down and shows you are listening. You don't have to agree to hear it back."),
            new("their-words", ExerciseStepKind.Text, "What they said",
                "Think of something your partner said or asked recently that you want to respond to well. Write it as close to their words as you can remember.",
                "Stick to what was said. Leave out what you think they meant for now."),
            new("in-my-words", ExerciseStepKind.Text, "In your own words",
                "Now say it back in your own words, without adding your side. You might start with \"What I'm hearing is…\"",
                "Try to keep it shorter than what they said."),
            new("what-matters", ExerciseStepKind.Text, "What matters to them",
                "What do you think matters most to your partner in this?",
                "A feeling, a worry, a hope, or something they need."),
            new("check-question", ExerciseStepKind.Text, "A question to check",
                "Is there anything you're unsure about? Write one question you could ask to check you understood.",
                Optional: true),
            new("noticed", ExerciseStepKind.MultiChoice, "What you noticed",
                "What did you notice in yourself while doing this? Choose any that fit.",
                Options:
                [
                    new("defend", "I wanted to explain or defend myself"),
                    new("calmer", "I felt calmer"),
                    new("new", "I understood something new"),
                    new("stuck", "I felt stuck"),
                    new("none", "Nothing in particular"),
                ]),
        ]);

    public static readonly ExerciseTemplate FeelingsAndNeeds = new(
        "feelings-and-needs",
        1,
        "Name it: feelings and needs",
        "Put words to what you felt in a moment that stayed with you, and to what you needed underneath.",
        10,
        [
            new("intro", ExerciseStepKind.Info, "Why name it",
                "Feelings that have a name are easier to share without blame. Underneath most strong feelings is something we need. "
                + "This exercise helps you find both, privately, before you decide whether to say anything."),
            new("moment", ExerciseStepKind.Text, "The moment",
                "Describe a recent moment with your partner that stayed with you. Write only what happened, as a camera would have seen it.",
                "For example: \"You looked at your phone while I was talking about my day.\" Leave out guesses about why."),
            new("feelings", ExerciseStepKind.MultiChoice, "What you felt",
                "Which of these come closest to what you felt? Choose any that fit.",
                Options:
                [
                    new("hurt", "Hurt"),
                    new("lonely", "Lonely"),
                    new("anxious", "Anxious or worried"),
                    new("frustrated", "Frustrated"),
                    new("disappointed", "Disappointed"),
                    new("overwhelmed", "Overwhelmed"),
                    new("embarrassed", "Embarrassed"),
                    new("sad", "Sad"),
                    new("angry", "Angry"),
                    new("unappreciated", "Unappreciated"),
                    new("hopeful", "Hopeful"),
                    new("relieved", "Relieved"),
                ]),
            new("feelings-other", ExerciseStepKind.Text, "Anything else you felt",
                "If none of those fit, or something else was there too, write it here.",
                Optional: true, MaxLength: 500),
            new("needs", ExerciseStepKind.MultiChoice, "What you needed",
                "What might you have needed in that moment? Choose any that fit.",
                Options:
                [
                    new("heard", "To be heard"),
                    new("respect", "Respect"),
                    new("reassurance", "Reassurance"),
                    new("closeness", "Closeness"),
                    new("rest", "Rest"),
                    new("fairness", "Fairness"),
                    new("honesty", "Honesty"),
                    new("appreciation", "Appreciation"),
                    new("space", "Some space"),
                    new("support", "Support"),
                    new("fun", "Fun or lightness"),
                    new("security", "Security"),
                ]),
            new("together", ExerciseStepKind.Text, "Put it together",
                "Write one or two sentences that join the moment, the feeling and the need, in your own words.",
                "One way to shape it: \"When …, I felt …, because I need …\""),
        ]);

    public static readonly ExerciseTemplate PauseBeforeReply = new(
        "pause-before-reply",
        1,
        "Pause before you reply",
        "When you feel stirred up, take a few minutes to sort out what you want to say before you say it. Nothing here is sent.",
        8,
        [
            new("intensity", ExerciseStepKind.Choice, "Right now",
                "How strong is the feeling right now?",
                Options:
                [
                    new("calm", "Fairly calm"),
                    new("stirred", "A bit stirred up"),
                    new("very", "Very stirred up"),
                    new("overwhelmed", "Overwhelmed"),
                ]),
            new("breathe", ExerciseStepKind.Info, "Take a breath",
                "If you're very stirred up, it's fine to stop here and come back later; your answers are saved. "
                + "If you want to keep going, try breathing out slowly a few times, or get up and move for a minute, before the next step."),
            new("unfiltered", ExerciseStepKind.Text, "Say it unfiltered",
                "What do you want to say right now, without editing? This is only for you.",
                "Getting it out of your head often makes the next steps easier."),
            new("understand", ExerciseStepKind.Text, "What you most want understood",
                "If your partner could understand just one thing about how you feel, what would it be?"),
            new("outcome", ExerciseStepKind.Text, "What you hope for",
                "What would you like to happen after you reply?",
                "For example: to feel closer, to agree a plan, or simply to be heard."),
            new("rewrite", ExerciseStepKind.Text, "Your reply",
                "Write the reply you'd want to send, with what you most want understood and what you hope for in mind."),
            new("when", ExerciseStepKind.Choice, "When",
                "What feels right for now?",
                Options:
                [
                    new("now", "I'm ready to reply"),
                    new("later", "I'll wait and come back to it"),
                    new("not", "I don't think I'll send anything"),
                ]),
        ]);

    public static readonly ExerciseTemplate RepairAttempt = new(
        "repair-attempt",
        1,
        "Reach back after a rough moment",
        "After an argument or a cold patch, plan a small step back towards each other.",
        10,
        [
            new("intro", ExerciseStepKind.Info, "Why reach back",
                "Every couple has rough moments. What helps is how you come back from them. "
                + "A small gesture, offered sincerely, can matter more than settling who was right. "
                + "If a rough moment involved threats, fear or anyone being hurt, please put your safety first and contact local emergency services or a domestic abuse support service."),
            new("what-happened", ExerciseStepKind.Text, "What happened",
                "Briefly, what happened?",
                "A few sentences is enough."),
            new("my-part", ExerciseStepKind.Text, "Your part",
                "What part of it do you see as yours? Not all of it, just your share.",
                "Owning a part doesn't mean taking all the blame."),
            new("their-side", ExerciseStepKind.Text, "Their side",
                "What do you imagine your partner might have felt?"),
            new("ways", ExerciseStepKind.MultiChoice, "Ways to reach back",
                "Which of these might suit the two of you? Choose any that fit.",
                Options:
                [
                    new("apology", "Apologise for my part"),
                    new("touch", "A hug or a gentle touch, if welcome"),
                    new("humour", "A bit of shared humour"),
                    new("do-over", "Ask for a do-over of the conversation"),
                    new("appreciate", "Say something I appreciate about them"),
                    new("break", "Suggest a break and a time to talk again"),
                ]),
            new("words", ExerciseStepKind.Text, "What you could say or do",
                "Write what you could say or do."),
            new("timing", ExerciseStepKind.Text, "When",
                "When might be a good time?",
                Optional: true, MaxLength: 300),
        ]);

    /// <summary>Every published exercise version. Never remove an entry that attempts may use.</summary>
    public static IReadOnlyList<ExerciseTemplate> All { get; } = [ReflectiveListening, FeelingsAndNeeds, PauseBeforeReply, RepairAttempt];
}
