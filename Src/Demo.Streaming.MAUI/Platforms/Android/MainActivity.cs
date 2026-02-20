///////////////////////////////////////////////////////////////////////////////
//
// Copyright (c) 2016-2026 VASTreaming
//
// Licensee is granted permission to use, copy and modify this file.
// Licensee can distribute and sell this file in a binary form as a part of the
// licensee's product. Licensee is prohibited from selling this file and
// library separately from the licensee's products. Licensee is prohibited from
// disclosing this file to any 3rd party. Licensee is prohibited from openly
// publishing this file as a part of open-source software or any other means.
//
///////////////////////////////////////////////////////////////////////////////

namespace VAST.Demo
{

    using Android;
    using Android.App;
    using Android.Content;
    using Android.Content.PM;
    using Android.Media.Projection;
    using Android.OS;
    using Android.Runtime;

    using System.Runtime.Versioning;

    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [SupportedOSPlatform("android")]
    public class MainActivity : MauiAppCompatActivity
    {

        private const int PERMISSIONS_REQUEST_CAPTURE = 1;
        private const int PERMISSIONS_REQUEST_MEDIA_PROJECTION = 2;

        public static MediaProjectionManager mediaProjectionManager = null;
        public static Result mediaProjectionResultCode = 0;
        public static Intent mediaProjectionResultData = null;

        protected override void OnResume()
        {

            base.OnResume();
            VAST.Common.GlobalContext.Context = this.ApplicationContext;
            VAST.Common.GlobalContext.MainActivity = this;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {

#pragma warning disable CA1416

                if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted ||
                    CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
                {

                    List<string> req = new List<string>();
                    if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted)
                    {
                        req.Add(Manifest.Permission.Camera);
                    }
                    if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
                    {
                        req.Add(Manifest.Permission.RecordAudio);
                    }
                    if (CheckSelfPermission(Manifest.Permission.ReadExternalStorage) != Permission.Granted)
                    {
                        req.Add(Manifest.Permission.ReadExternalStorage);
                    }
                    if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
                    {
                        req.Add(Manifest.Permission.WriteExternalStorage);
                    }

                    RequestPermissions(req.ToArray(), PERMISSIONS_REQUEST_CAPTURE);

                }

#pragma warning restore CA1416

                //if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
                //{
                //    mediaProjectionManager = (MediaProjectionManager)GetSystemService(Context.MediaProjectionService);
                //    if (mediaProjectionResultCode != Result.Ok || mediaProjectionResultData == null)
                //    {
                //        StartActivityForResult(mediaProjectionManager.CreateScreenCaptureIntent(), PERMISSIONS_REQUEST_MEDIA_PROJECTION);
                //    }
                //}

            }

        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {

            if (requestCode == PERMISSIONS_REQUEST_MEDIA_PROJECTION)
            {

                if (resultCode != Result.Ok)
                {
                    return;
                }

                mediaProjectionResultCode = resultCode;
                mediaProjectionResultData = data;

                // TODO: uncomment to start service
                //Intent intent = new Intent(VAST.Common.GlobalContext.MainActivity, typeof(CaptureService));
                //VAST.Common.GlobalContext.MainActivity.StartService(intent);

            }

            base.OnActivityResult(requestCode, resultCode, data);

        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {

            switch (requestCode)
            {

                case PERMISSIONS_REQUEST_CAPTURE:
                    {
                        bool allGranted = true;
                        foreach (Permission res in grantResults)
                        {
                            if (res != Permission.Granted)
                            {
                                allGranted = false;
                                break;
                            }
                        }
                        if (allGranted)
                        {
                        }
                        break;
                    }

                default:
#pragma warning disable CA1416
                    base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
#pragma warning restore CA1416
                    break;

            }

        }

    }

}
