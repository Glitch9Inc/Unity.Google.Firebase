using System;
using Firebase.Auth;

namespace Glitch9.Apis.Google.Firebase
{
    public class FirebaseEventHandler
    {
        public EventHandler OnInternetConnectionError { get; set; }
        public EventHandler<string> OnSignInError { get; set; }
        public EventHandler<FirebaseUser> OnSignedIn { get; set; }
        public EventHandler OnSignedOut { get; set; }
        public EventHandler<FirebaseUser> OnAccountCreated { get; set; }
    }
}