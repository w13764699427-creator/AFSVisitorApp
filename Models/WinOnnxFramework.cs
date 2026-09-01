using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace DEV.Win
{
    public class WinOnnxFramework : IInferenceFramework
    {
        private InferenceSession session = null;
        private Microsoft.ML.OnnxRuntime.SessionOptions options = null;
        public bool Init(string category, string model, Size size, InferenceMode inferenceMode)
        {

            var modelsDir = Path.Combine(Globals.DataDir, "models", category, "onnx");
            string modelFile = Path.Combine(modelsDir, $"{model}.onnx");
            if (System.IO.File.Exists(modelFile))
            {
                var modelData = File.ReadAllBytes(modelFile);

                if (inferenceMode == InferenceMode.GPU)
                {
                    options = new Microsoft.ML.OnnxRuntime.SessionOptions();
                    options.AppendExecutionProvider_CUDA(0);
                }
                else if (inferenceMode == InferenceMode.Auto)
                {
                    options = new Microsoft.ML.OnnxRuntime.SessionOptions();
                    options.AppendExecutionProvider_CUDA(0);
                }

                if (options != null)
                {
                    session = new InferenceSession(modelData, options);
                }
                else
                {
                    session = new InferenceSession(modelData);
                }
            }
            return session != null;
        }
        public List<KeyValuePair<string, float[]>> Inference(Mat mat, Memory<float> data)
        {
            if (session != null)
            {
                int[] dimensions = new int[] { mat.Size(0), mat.Size(1), mat.Size(2), mat.Size(3) };

                var inputTensor = new DenseTensor<float>(data, dimensions);

                var inputName = session.InputMetadata.Keys.FirstOrDefault();
                var inputs = new List<NamedOnnxValue>
                        {
                            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
                        };
                var results = session.Run(inputs);
                var list = new List<KeyValuePair<string, float[]>>();
                foreach (var result in results)
                {
                    list.Add(new KeyValuePair<string, float[]>(result.Name, result.AsTensor<float>().ToArray()));
                }
                return list;

            }
            return null;
        }
        public Mat PreprocessImage(Mat mat, int width, int height)
        {
            if (mat.Width > width || mat.Height > height)
            {
                using var img = FaceHelper.CropToSize(mat, new Size(width, height), true);
                return img.CvtColor(ColorConversionCodes.BGR2RGB);
            }
            else
            {
                using var img = FaceHelper.PadToSize(mat, new Size(width, height));
                return img.CvtColor(ColorConversionCodes.BGR2RGB);
            }
        }
        public Mat BlobFromImage(Mat mat,double scale, Size size, Scalar scalar)
        {
            return CvDnn.BlobFromImage(mat, scale, size, scalar, false, false);
        }

    }
}
