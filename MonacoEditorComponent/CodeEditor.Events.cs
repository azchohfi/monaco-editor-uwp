﻿using Monaco.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Newtonsoft.Json.Linq;

namespace Monaco
{
    public partial class CodeEditor
    {
        // Override default Loaded/Loading event so we can make sure we've initialized our WebView contents with the CodeEditor.

        /// <summary>
        /// When Editor is Loading, it is ready to receive commands to the Monaco Engine.
        /// </summary>
        public new event RoutedEventHandler Loading;

        /// <summary>
        /// When Editor is Loaded, it has been rendered and is ready to be displayed.
        /// </summary>
        public new event RoutedEventHandler Loaded;

        /// <summary>
        /// Called when a link is Ctrl+Clicked on in the editor, set Handled to true to prevent opening.
        /// </summary>
        //public event TypedEventHandler<WebView2, WebView2NewWindowRequestedEventArgs> OpenLinkRequested;

        /// <summary>
        /// Called when an internal exception is encountered while executing a command. (for testing/reporting issues)
        /// </summary>
        public event TypedEventHandler<CodeEditor, Exception> InternalException;

        /// <summary>
        /// Custom Keyboard Handler.
        /// </summary>
        public new event WebKeyEventHandler KeyDown;

        private ThemeListener _themeListener;

        //private void WebView_DOMContentLoaded(WebView2 sender, WebView2DOMContentLoadedEventArgs args)
        //{
        //    #if DEBUG
        //    Debug.WriteLine("DOM Content Loaded");
        //    #endif
        //    this._initialized = true;
        //}

        private async void WebView_NavigationCompleted(WebView2 sender, WebView2NavigationCompletedEventArgs args)
        {
            this.IsLoaded = true;

            // Make sure inner editor is focused
            await SendScriptAsync("editor.focus();");

            // If we're supposed to have focus, make sure we try and refocus on our now loaded webview.
            if (FocusManager.GetFocusedElement() == this)
            {
                this._view.Focus(FocusState.Programmatic);
            }

            Loaded?.Invoke(this, new RoutedEventArgs());
        }

        internal ParentAccessor _parentAccessor;
        private KeyboardListener _keyboardListener;
        private long _themeToken;

        private void WebView_NavigationStarting(WebView2 sender, WebView2NavigationStartingEventArgs args)
        {
#if DEBUG
            Debug.WriteLine("Navigation Starting");
#endif
            _parentAccessor = new ParentAccessor(this);
            _parentAccessor.AddAssemblyForTypeLookup(typeof(Range).GetTypeInfo().Assembly);
            _parentAccessor.RegisterAction("Loaded", CodeEditorLoaded);

            _themeListener = new ThemeListener();
            _themeListener.ThemeChanged += _themeListener_ThemeChanged;
            _themeToken = RegisterPropertyChangedCallback(RequestedThemeProperty, RequestedTheme_PropertyChanged);

            _keyboardListener = new KeyboardListener(this);

            _allowedObject.Clear();

            AddWebAllowedObject("Debug", new DebugLogger());
            AddWebAllowedObject("Parent", _parentAccessor);
            AddWebAllowedObject("Theme", _themeListener);
            AddWebAllowedObject("Keyboard", _keyboardListener);
        }

        private Dictionary<string, object> _allowedObject = new Dictionary<string, object>();

        internal void AddWebAllowedObject(string name, object pObject)
        {
            _allowedObject.Add(name, pObject);
        }

        private async void WebView_WebMessageReceived(WebView2 sender, WebView2WebMessageReceivedEventArgs e)
        {
            Debug.WriteLine(e.WebMessageAsString);

            try
            {
                JObject result = JObject.Parse(e.WebMessageAsString);

                if (result.TryGetValue("class", out var name) &&
                    _allowedObject.TryGetValue(name.Value<string>(), out var obj))
                {
                    var method = result.GetValue("method").Value<string>();
                    switch (obj)
                    {
                        case DebugLogger debugLogger:
                            switch (method)
                            {
                                case "Log":
                                    debugLogger.Log(result.GetValue("p1").Value<string>());
                                    break;
                            }
                            break;
                        case ParentAccessor parentAccessor:
                            switch (method)
                            {
                                case "CallEvent":
                                    await parentAccessor.CallEvent(result.GetValue("p1").Value<string>(), result.GetValue("p2").Value<string[]>());
                                    break;
                                case "CallAction":
                                    parentAccessor.CallAction(result.GetValue("p1").Value<string>());
                                    break;
                                case "GetValue":
                                    parentAccessor.GetValue(result.GetValue("p1").Value<string>());
                                    break;
                                case "GetJsonValue":
                                    parentAccessor.GetJsonValue(result.GetValue("p1").Value<string>());
                                    break;
                                case "GetChildValue":
                                    parentAccessor.GetChildValue(result.GetValue("p1").Value<string>(), result.GetValue("p2").Value<string>());
                                    break;
                                case "SetValue":
                                    if (result.TryGetValue("p3", out var p3))
                                    {
                                        parentAccessor.SetValue(result.GetValue("p1").Value<string>(), result.GetValue("p2").Value<string>(), p3.Value<string>());
                                    }
                                    else
                                    {
                                        parentAccessor.SetValue(result.GetValue("p1").Value<string>(), result.GetValue("p2").Value<string>());
                                    }
                                    break;
                            }
                            break;
                        case ThemeListener themeListener:
                            switch (method)
                            {
                                case "CurrentThemeName":
                                    var x = themeListener.CurrentThemeName;
                                    break;
                                case "IsHighContrast":
                                    var y = themeListener.IsHighContrast;
                                    break;
                            }
                            break;
                        case KeyboardListener keyboardListener:
                            switch (method)
                            {
                                case "KeyDown":
                                    keyboardListener.KeyDown(result.GetValue("keyCode").Value<int>(),
                                        result.GetValue("ctrlKey").Value<bool>(),
                                        result.GetValue("shiftKey").Value<bool>(),
                                        result.GetValue("altKey").Value<bool>(),
                                        result.GetValue("metaKey").Value<bool>());
                                    break;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private async void CodeEditorLoaded()
        {
            if (Decorations != null && Decorations.Count > 0)
            {
                // Need to retrigger highlights after load if they were set before load.
                await DeltaDecorationsHelperAsync(Decorations.ToArray());
            }

            // Now we're done loading
            Loading?.Invoke(this, new RoutedEventArgs());
        }

        //private void WebView_NewWindowRequested(WebView2 sender, WebView2NewWindowRequestedEventArgs args)
        //{
        //    // TODO: Should probably create own event args here as we don't want to expose the referrer to our internal page?
        //    OpenLinkRequested?.Invoke(sender, args);
        //}

        private async void RequestedTheme_PropertyChanged(DependencyObject obj, DependencyProperty property)
        {
            var editor = obj as CodeEditor;
            var theme = editor.RequestedTheme;
            var tstr = string.Empty;

            if (theme == ElementTheme.Default)
            {
                tstr = _themeListener.CurrentThemeName;
            }
            else
            {
                tstr = theme.ToString();
            }

            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                await this.ExecuteScriptAsync("changeTheme", new string[] { tstr, _themeListener.IsHighContrast.ToString() });
            });
        }

        private async void _themeListener_ThemeChanged(ThemeListener sender)
        {
            if (RequestedTheme == ElementTheme.Default)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await this.ExecuteScriptAsync("changeTheme", args: new string[] { sender.CurrentTheme.ToString(), sender.IsHighContrast.ToString() });
                });
            }
        }

        internal bool TriggerKeyDown(WebKeyEventArgs args)
        {
            this.KeyDown?.Invoke(this, args);

            return args.Handled;
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);

            if (this._view != null && FocusManager.GetFocusedElement() == this)
            {
                // Forward Focus onto our inner WebView
                this._view.Focus(FocusState.Programmatic);
            }
        }
    }
}
