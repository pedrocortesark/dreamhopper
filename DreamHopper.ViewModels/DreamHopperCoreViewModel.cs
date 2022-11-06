using DreamHopper.IO;
using DreamHopper.MVVM;
using DreamHopper.ViewModels.Helpers;
using DreamHopper.ViewModels.ViewModels;
using DreamHopper.WebClient;
using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;

namespace DreamHopper.ViewModels
{
    public class DreamHopperCoreViewModel : BaseViewModel
    {
        public static DreamHopperCoreViewModel Instance;
        public string DocName { get; private set; }
        public bool OutputsExpanded { get; set; }
        public bool Processing { get; set; }

        public ICommand CaptureImageCommand { get; set; }
        public ICommand OpenImageCommand { get; set; }
        public ICommand RunInferenceCommand { get; set; }
        public ICommand SaveImagesCommand { get; set; }

        private InferenceClient _client;
        private SubmissionReceipt _receipt;
        public SnackbarMessageQueue MessageQueue { get; set; }
        public Bitmap ReferenceImage { get; set; }
        public bool HasReferenceImage { get { return ReferenceImage != null; } set { } }

        public ObservableCollection<BaseInputViewModel> Inputs { get; set; }
        public ObservableCollection<OutputCardViewModel> Outputs { get; set; }

        private object _outputsLock = new object();

        public DreamHopperCoreViewModel(string docName)
        {
            Instance = this;
            this.DocName = docName;
            //this.CaptureImageCommand = new RelayCommand(this.CaptureImage);
            //this.OpenImageCommand = new RelayCommand(this.OpenImage);
            this.RunInferenceCommand = new RelayCommand(async () => await this.RunInference());
            this.SaveImagesCommand = new RelayCommand(this.SaveImages);
            this._client = new InferenceClient();
            this.MessageQueue = new SnackbarMessageQueue(new TimeSpan(0, 0, 3));
            this.Outputs = new ObservableCollection<OutputCardViewModel>();
            BindingOperations.EnableCollectionSynchronization(this.Outputs, _outputsLock);

            this.Inputs = new ObservableCollection<BaseInputViewModel>()
            {
                new TextInputViewModel("Prompt:", "text"),
                new NumberSliderInputViewModel("Iterations:", "iters", 0, 50000, 2000, 1),
                new NumberSliderInputViewModel("Clip Size:", "clip", 128, 768, 384, 8),
                new NumberSliderInputViewModel("Threshold:", "threshold", 0, 0, 768, 0.1),
                new IntegerInputViewModel("Seed:", "seed", -1)
            };

            this.PropertyChanged += DreamHopperCoreViewModel_PropertyChanged;
        }

        //private void OpenImage()
        //{
        //    using (OpenFileDialog openFileDialog = new OpenFileDialog())
        //    {
        //        openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        //        openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
        //        openFileDialog.FilterIndex = 2;
        //        openFileDialog.RestoreDirectory = true;

        //        if (openFileDialog.ShowDialog() == DialogResult.OK)
        //        {
        //            try
        //            {
        //                ReferenceImage = new Bitmap(openFileDialog.FileName);
        //            }
        //            catch (Exception exc)
        //            {
        //                this.MessageQueue.Enqueue(SnackBarContentCreator.CreateErrorMessage(exc.Message));
        //            }
        //        }
        //    }
        //}


        private void DreamHopperCoreViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(this.ReferenceImage))
            {
                this.RaisePropertyChanged(nameof(this.HasReferenceImage));
            }
        }

        public void CaptureImage()
        {
            CaptureImageEventArgs args = new CaptureImageEventArgs(512, 512);
            this.OnImageCaptureTriggered(args);
        }

        public void SaveImages()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Title = "Save Generated Images",

                DefaultExt = "png",
                Filter = "PNG Image (*.png)|*.png",
                FilterIndex = 2,
                RestoreDirectory = true
            };

            List<OutputCardViewModel> selectedCards = this.Outputs.Where(s => s.IsSelected).ToList();

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                for (int i = 1; i <= selectedCards.Count; i++)
                {
                    string name = saveFileDialog.FileName;
                    name = name.Replace(".png", $"_{i.ToString("0000")}.png");

                    var card = selectedCards[i - 1];
                    card.Image.Save(name, ImageFormat.Png);
                }
            }
        }

        public async Task RunInference()
        {
            this.Outputs.Clear();
            Dictionary<string, object> dict = new Dictionary<string, object>();

            foreach (BaseInputViewModel vm in this.Inputs)
            {
                KeyValuePair<string, object> entry = vm.GetValuePair();
                dict.Add(entry.Key, entry.Value);
            }

            //string inputImage = this.InputImageAsBase64();
            string prompt = dict["text"] as string ?? "";
            string user = Environment.UserName;
            int iterations = (int)dict["iters"];
            int clip = (int)dict["clip"];
            int seed = (int)dict["seed"];
            double threshold = (double)dict["threshold"];

            //Communicate the server
            DreamHopperDTO dto = new DreamHopperDTO(
                prompt,
                user,
                iterations,
                clip,
                threshold,
                seed);

            try
            {
                _receipt = await this._client.SubmitRequest(dto);
            }
            catch (Exception exe)
            {
                this.MessageQueue.Enqueue(SnackBarContentCreator.CreateErrorMessage(exe.Message));
                this.Processing = false;
            }

            if (_receipt != null)
            {
                Task.Run(async () =>
                {
                    this.Processing = true;
                    this.OutputsExpanded = true;
                    bool isDone = false;
                    DiffusionDTO response = new DiffusionDTO();

                    int errorCounter = 0;

                    while (!isDone)
                    {
                        try
                        {
                            response = await this._client.CheckRequestStatus(this._receipt);
                            isDone = response.Done;
                            Thread.Sleep(this._client.CheckFrequency);
                        }
                        catch (Exception e)
                        {
                            this.MessageQueue.Enqueue(SnackBarContentCreator.CreateErrorMessage(e.Message));
                            errorCounter += 1;
                            if (errorCounter > 10) isDone = true;
                        }
                    }
                    this.Processing = false;

                    lock (_outputsLock)
                    {
                        foreach (string img in response.OutputImages)
                        {
                            try
                            {
                                this.Outputs.Add(new OutputCardViewModel(this.OutputImageAsBitmap(img)));
                            }
                            catch (Exception exc)
                            {
                                this.MessageQueue.Enqueue(SnackBarContentCreator.CreateErrorMessage(exc.Message));
                            }
                        }
                    }
                });
            }


        }

        public string InputImageAsBase64()
        {
            if (this.ReferenceImage != null)
            {
                MemoryStream ms = new MemoryStream();
                this.ReferenceImage.Save(ms, ImageFormat.Png);
                byte[] byteImage = ms.ToArray();
                return Convert.ToBase64String(byteImage); // Get Base64
            }
            else
            {
                return string.Empty;
            }
        }

        public Bitmap OutputImageAsBitmap(string base64)
        {
            Byte[] bitmapData = Convert.FromBase64String(base64);
            System.IO.MemoryStream streamBitmap = new System.IO.MemoryStream(bitmapData);
            return new Bitmap((Bitmap)Image.FromStream(streamBitmap));
        }


        public EventHandler ImageCaptureTriggered;
        public void OnImageCaptureTriggered(CaptureImageEventArgs e)
        {
            EventHandler handler = ImageCaptureTriggered;
            handler?.Invoke(this, e);
        }
    }
}