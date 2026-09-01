using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenVinoSharp;
using System.Runtime.InteropServices;

namespace DEV.Win
{
    public class WinOpenVINOFramework : IInferenceFramework
    {
        private Core core = null;
        private Model model = null;
        private CompiledModel compiledModel = null;
        private InferRequest session = null;

        private List<Output> outputs = new List<Output>();

        public bool Init(string category, string modelName, Size size, InferenceMode inferenceMode)
        {
            var modelsDir = Path.Combine(Globals.DataDir, "models", category, "openvino");
            string xmlModelFile = Path.Combine(modelsDir, $"{modelName}.xml");
            string binModelFile = Path.Combine(modelsDir, $"{modelName}.bin");

            if (File.Exists(xmlModelFile) && File.Exists(binModelFile))
            {
                core = new Core();

                // 【关键修改 1】：直接从文件读取 XML，OpenVINO 会自动在同级目录查找同名的 .bin 权重文件
                // 这是最稳定、高效的方式，也支持 INT8/FP16 混合精度的模型
                model = core.read_model(xmlModelFile);

                /* 
                 * 如果因为加密等原因必须从内存 Byte[] 加载，请使用下面的写法替代上面的 read_model：
                 * byte[] xmlData = File.ReadAllBytes(xmlModelFile);
                 * byte[] binData = File.ReadAllBytes(binModelFile);
                 * using Tensor weightsTensor = new Tensor(new ElementType(ElementType.e_u8), new Shape(new long[] { binData.Length }), binData);
                 * model = core.read_model(Encoding.UTF8.GetString(xmlData), weightsTensor);
                */

                // 2. 重新指定输入的 Shape [Batch, Channel, Height, Width]
                model.reshape(new Shape(new long[] { 1, 3, size.Height, size.Width }));

                // 3. 按照指定模式编译模型
                string deviceName = inferenceMode switch
                {
                    InferenceMode.Auto => "AUTO:GPU,CPU",
                    InferenceMode.CPU => "CPU",
                    InferenceMode.GPU => "GPU",
                    InferenceMode.NPU => "NPU",
                    _ => "CPU"
                };

                compiledModel = core.compile_model(model, deviceName);
                session = compiledModel.create_infer_request();

                outputs.Clear();
                outputs.AddRange(model.outputs());
            }

            return session != null;
        }

        public List<KeyValuePair<string, float[]>> Inference(Mat mat, Memory<float> data)
        {
            if (session != null && mat != null && !mat.Empty())
            {
                //// 【关键修改 2】：计算 NCHW float 数据的真实总元素量 (Batch * Channel * Height * Width)
                //int totalElements = (int)(mat.Total() * mat.Channels());
                //float[] inputData = new float[totalElements];

                //// 将指针指向的 Blob (float32) 连续内存数据拷贝到 float 数组中
                //Marshal.Copy(mat.Data, inputData, 0, totalElements);

                // 获取输入 Tensor 并填充数据
                Tensor inputTensor = session.get_input_tensor();
                inputTensor.set_data(data.Span);

                // 运行推理
                session.infer();

                var list = new List<KeyValuePair<string, float[]>>();
                for (int i = 0; i < outputs.Count; i++)
                {
                    var ot = outputs[i];
                    var name = ot.GetAnyName();

                    // 获取对应的输出 Tensor 数据
                    var output = session.get_output_tensor((ulong)i);

                    // 填充结果列表
                    list.Add(new KeyValuePair<string, float[]>(name, output.get_data<float>((int)output.size)));
                }
                return list;
            }
            return null;
        }

        public Mat PreprocessImage(Mat mat, int width, int height)
        {
            using var img = FaceHelper.CropToSize(mat, new Size(width, height), true);
            return img.CvtColor(ColorConversionCodes.BGR2RGB);
        }

        public Mat BlobFromImage(Mat mat, double scale, Size size,Scalar scalar)
        {
            // CvDnn.BlobFromImage 会将 Mat 转换为 [1, 3, size.Height, size.Width] 的 NCHW 布局 float32 Mat
            return CvDnn.BlobFromImage(mat, scale, size, scalar, swapRB: false, crop: false);
        }
    }
}