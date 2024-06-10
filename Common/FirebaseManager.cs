using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using System;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
#elif UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace Glitch9.Apis.Google.Firebase
{
    /// <summary>
    /// Manages Firebase authentication and user data.
    /// </summary>
    public static class FirebaseManager
    {
        private static Prefs<string> _savedUserId;
        private static Prefs<string> _savedPassword;
        private static Prefs<string> _savedEmail;
        private static Prefs<string> _savedPhotoUrl;
        private static Uri _savedPhotoUri;

        private static FirebaseAuth _auth;
        public static FirebaseAuth Auth => _auth ??= FirebaseAuth.DefaultInstance;

        private static FirebaseUser _user;
        public static FirebaseUser User
        {
            get
            {
                if (_user == null && Auth != null && Auth.CurrentUser != null)
                    _user = Auth.CurrentUser;
                return _user;
            }
            set => _user = value;
        }

        public static bool IsSignedIn => User != null && _isInitialized;
        public static string UserId => User != null ? User.UserId : _savedUserId.Value;
        public static string Email => User != null ? User.Email : _savedEmail.Value;
        public static string DisplayName => User != null ? User.DisplayName : string.Empty;
        public static Uri PhotoUrl => User != null ? User.PhotoUrl : _savedPhotoUri;

        private static bool _isInitialized = false;
        private static ILogger _logger;
        private static FirebaseEventHandler _eventHandler;

        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>
        /// Initializes the FirebaseManager with optional event handler and logger.
        /// </summary>
        public static void Initialize(FirebaseEventHandler eventHandler = null, ILogger logger = null)
        {
            if (_isInitialized)
            {
                _logger.Warning(Strings.FirebaseManagerAlreadyInitialized);
                return;
            }

            _logger = logger ?? new FirebaseLogger();
            string projectName = FirebaseSettings.ProjectName;

            if (string.IsNullOrEmpty(projectName))
            {
                _logger.Error(Strings.ProjectNameNotSet);
                return;
            }

            _eventHandler = eventHandler;

            string prefsKeyUserId = $"{projectName}.UserId";
            string prefsKeyPassword = $"{projectName}.Password";
            string prefsKeyEmail = $"{projectName}.Email";
            string prefsKeyPhotoUrl = $"{projectName}.PhotoUrl";

            _savedUserId = new Prefs<string>(prefsKeyUserId);
            _savedPassword = new Prefs<string>(prefsKeyPassword);
            _savedEmail = new Prefs<string>(prefsKeyEmail);
            _savedPhotoUrl = new Prefs<string>(prefsKeyPhotoUrl);
            _savedPhotoUri = new Uri(_savedPhotoUrl.Value);

            CheckAndFixDependenciesAsync();
        }

        /// <summary>
        /// Retries to check and fix Firebase dependencies.
        /// </summary>
        public static void Retry()
        {
            CheckAndFixDependenciesAsync();
        }

        private static async void CheckAndFixDependenciesAsync()
        {
            if (!CheckInternetConnection())
            {
                _eventHandler?.OnInternetConnectionError?.Invoke(nameof(FirebaseManager), EventArgs.Empty);
                return;
            }

            try
            {
                await _semaphore.WaitAsync();
                DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (dependencyStatus == DependencyStatus.Available)
                {
                    _logger.Info(Strings.FirebaseDependenciesResolved + dependencyStatus);
                    Auth.StateChanged += AuthStateChanged;
                }
                else
                {
                    string errorMessage = Strings.FirebaseDependenciesNotResolved + dependencyStatus;
                    _logger.Error(errorMessage);
                    _eventHandler?.OnSignInError?.Invoke(nameof(FirebaseManager), errorMessage);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e.Message);
                _eventHandler?.OnSignInError?.Invoke(nameof(FirebaseManager), e.Message);
            }
            finally
            {
                _isInitialized = true;
                _semaphore.Release();
            }
        }

        private static void AuthStateChanged(object sender, EventArgs eventArgs)
        {
            if (User == null)
            {
                _logger.Info(Strings.UserSignedOut);
                _eventHandler?.OnSignedOut?.Invoke(nameof(FirebaseManager), EventArgs.Empty);
            }
            else
            {
                _logger.Info(Strings.UserSignedIn);
                _eventHandler?.OnSignedIn?.Invoke(nameof(FirebaseManager), User);
            }
        }

        /// <summary>
        /// Signs out the current user.
        /// </summary>
        public static void SignOut()
        {
            if (Auth == null)
            {
                _logger.Warning(Strings.NotSignedInCannotSignOut);
                return;
            }
            Auth.SignOut();

#if UNITY_EDITOR
#elif UNITY_ANDROID
            PlayGamesPlatform.Instance?.SignOut();
#endif      
        }

        /// <summary>
        /// Closes the application.
        /// </summary>
        private static void CloseApp()
        {
            Application.Quit();
        }

        /// <summary>
        /// Checks if the internet connection is available.
        /// </summary>
        public static bool CheckInternetConnection()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                _logger.Error(Strings.NoInternetConnection);
                return false;
            }
            else
            {
                _logger.Info(Strings.InternetConnectionAvailable);
                return true;
            }
        }

        /// <summary>
        /// Returns the current user's email if the provided email is null or empty.
        /// </summary>
        public static string ValidateEmail(string email)
        {
            email ??= _savedUserId;

            if (string.IsNullOrEmpty(email))
            {
                if (User != null && !string.IsNullOrEmpty(User.Email))
                {
                    return User.Email;
                }
                else
                {
                    string errorMessage = Strings.EmailNotValid;
                    _logger.Warning(errorMessage);
                    _eventHandler?.OnSignInError?.Invoke(nameof(FirebaseManager), errorMessage);
                    return null;
                }
            }
            return email;
        }

        /// <summary>
        /// Checks if Firebase Auth is valid.
        /// </summary>
        public static bool CheckFirebaseAuth()
        {
            string currentTokenEmail = _auth.CurrentUser.Email;
            if (string.IsNullOrEmpty(currentTokenEmail))
            {
                _logger.Warning(Strings.FirebaseAuthNotValid);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Signs in with the saved user information.
        /// </summary>
        public static async UniTask<FirebaseUser> SignInWithSavedInformation(Action<bool> onResult = null)
        {
            _logger.Info(Strings.LoggingInWithSavedInfo);
            string email = _savedEmail;
            string password = _savedPassword;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _logger.Info(Strings.NoSavedEmailOrPassword);
                return null;
            }

            return await SignInWithEmailAndPasswordAsync(email, password, onResult);
        }

        /// <summary>
        /// Signs in with the provided email and password.
        /// </summary>
        public static async UniTask<FirebaseUser> SignInWithEmailAndPasswordAsync(string email, string password, Action<bool> onResult = null)
        {
            _savedEmail.Value = email;
            _savedPassword.Value = password;

            try
            {
                AuthResult authResult = await Auth.SignInWithEmailAndPasswordAsync(_savedEmail, _savedPassword);
                FirebaseUser user = authResult.User;
                _logger.Info(Strings.UserSignedInSuccessfully + user.Email);
                onResult?.Invoke(true);
                _eventHandler?.OnSignedIn?.Invoke(nameof(FirebaseManager), user);
                return user;
            }
            catch (Exception ex)
            {
                _logger.Error(Strings.SignInWithEmailAndPasswordError + ex.Message);
                onResult?.Invoke(false);
                return null;
            }
        }

        /// <summary>
        /// Creates a new user with the provided email and password.
        /// </summary>
        public static void CreateUserWithEmailAndPassword(string email, string password, Action<string> onComplete = null)
        {
            _savedEmail.Value = email;
            _savedPassword.Value = password;

            Auth.CreateUserWithEmailAndPasswordAsync(_savedEmail, _savedPassword).ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    _logger.Error(Strings.CreateUserCanceled);
                    return;
                }
                if (task.IsFaulted)
                {
                    _logger.Error(Strings.CreateUserError + task.Exception);
                    return;
                }

                User = task.Result.User;
                _logger.Info(Strings.UserCreatedSuccessfully + User.Email);
                _eventHandler?.OnAccountCreated?.Invoke(nameof(FirebaseManager), User);
                onComplete?.Invoke(User.Email);
            });
        }

        /// <summary>
        /// Signs in using Google Play Games.
        /// </summary>
        public static async UniTask SignInWithPlayGamesAsync(Action<bool> onSuccess = null)
        {
#if UNITY_EDITOR
            await UniTask.Yield();
            _logger.Info(Strings.EditorPlayGamesSignIn);
#elif UNITY_ANDROID
            PlayGamesClientConfiguration config = new PlayGamesClientConfiguration.Builder()
                .RequestServerAuthCode(false)
                .RequestIdToken()
                .RequestEmail()
                .Build();

            PlayGamesPlatform.InitializeInstance(config);
            PlayGamesPlatform.DebugLogEnabled = true;
            PlayGamesPlatform.Activate();

            bool isAuthenticated = await AuthenticateUserAsync();
            if (!isAuthenticated)
            {
                onSuccess?.Invoke(false);
                return;
            }

            string idToken = PlayGamesPlatform.Instance.GetIdToken();
            string email = PlayGamesPlatform.Instance.GetUserEmail();
            await SignInWithGoogleOnFirebaseAsync(idToken, email, onSuccess);
#endif
        }

        private static async UniTask<bool> AuthenticateUserAsync()
        {
            UniTaskCompletionSource<bool> tcs = new();

            Social.localUser.Authenticate(success =>
            {
                if (!success)
                {
                    _logger.Error(Strings.PlayGamesAuthFailed);
                }
                tcs.TrySetResult(success);
            });

            return await tcs.Task;
        }

        private static async UniTask SignInWithGoogleOnFirebaseAsync(string idToken, string email, Action<bool> onSuccess)
        {
            Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
            try
            {
                FirebaseUser authResult = await Auth.SignInWithCredentialAsync(credential);
                User = authResult;
                _logger.Info(Strings.UserSignedInSuccessfully + User.UserId + " / " + email);
                _eventHandler?.OnSignedIn?.Invoke(nameof(FirebaseManager), User);
                onSuccess?.Invoke(true);
            }
            catch (Exception e)
            {
                _logger.Error(Strings.SignInWithGoogleError + e);
                _eventHandler?.OnSignInError?.Invoke(nameof(FirebaseManager), e.Message);
                onSuccess?.Invoke(false);
            }
        }

        /// <summary>
        /// Class containing constant string values for logging and messages.
        /// </summary>
        private static class Strings
        {
            internal const string FirebaseManagerAlreadyInitialized = "FirebaseManager is already initialized.";
            internal const string ProjectNameNotSet = "FirebaseManager: ProjectName is not set.";
            internal const string FirebaseDependenciesResolved = "Successfully resolved all Firebase dependencies: ";
            internal const string FirebaseDependenciesNotResolved = "Could not resolve all Firebase dependencies: ";
            internal const string UserSignedOut = "FirebaseManager: Signed out.";
            internal const string UserSignedIn = "Firebase: Signed in.";
            internal const string NotSignedInCannotSignOut = "Not signed in, cannot sign out.";
            internal const string NoInternetConnection = "No internet connection.";
            internal const string InternetConnectionAvailable = "Internet connection is available.";
            internal const string EmailNotValid = "Email is not valid.";
            internal const string FirebaseAuthNotValid = "Firestore auth is not valid.";
            internal const string LoggingInWithSavedInfo = "Logging in with saved information...";
            internal const string NoSavedEmailOrPassword = "No saved email or password found.";
            internal const string UserSignedInSuccessfully = "User signed in successfully: ";
            internal const string SignInWithEmailAndPasswordError = "SignInWithEmailAndPasswordAsync encountered an error: ";
            internal const string CreateUserCanceled = "CreateUserWithEmailAndPasswordAsync was canceled.";
            internal const string CreateUserError = "CreateUserWithEmailAndPasswordAsync encountered an error: ";
            internal const string UserCreatedSuccessfully = "Firebase user created successfully: ";
            internal const string EditorPlayGamesSignIn = "Editor: Google Play Games sign-in attempt (PlayGames cannot be used in the editor).";
            internal const string PlayGamesAuthFailed = "Google Play Games authentication failed.";
            internal const string SignInWithGoogleError = "SignInWithGoogleOnFirebaseAsync encountered an error: ";
        }
    }
}
