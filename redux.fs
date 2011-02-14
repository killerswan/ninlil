// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts


///////////////////////////////////////////////
// dependencies

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


///////////////////////////////////////////////
// command line args

let ( api, email, password ) = match System.Environment.GetCommandLineArgs() with
                                 | [| _; blog; email; password |] -> 
                                    ( ("http://" + blog + ".tumblr.com/api"), email, password )
                                 | _ -> 
                                    failwithf "Usage: mono exe BLOG EMAIL PASSWORD"


///////////////////////////////////////////////
// FETCH A URL

let getDocRaw (url:string) : string = 
   // This function doesn't return, at all, if the URL is wrong 
   // in some ways. TODO: fix. 

   let getpage (url:string) = 
      async {
         // see Expert F# at 383, etc.
         let req        = WebRequest.Create(url, Timeout=5)
         use! response  = req.AsyncGetResponse()
         use reader     = new StreamReader(response.GetResponseStream())
         return reader.ReadToEnd()
      }

   // get data as XML, return it
   (Async.RunSynchronously(getpage url))


///////////////////////////////////////////////
// SIMPLE QUERIES

// xml out
let readPosts (start:int) (num:int)       = getDocRaw <| api + "/read" +
                                                         "?start=" + (sprintf "%d" start) + 
                                                         "&num="   + (sprintf "%d" num) +
                                                         "&type="  + "photo"

// status out
let deletePost (id:string)                = getDocRaw <| api + "/delete" +
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id
                     
// new id out
let reblogPost (id:string) (rkey:string)  = getDocRaw <| api + "/reblog" + 
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id + 
                                                         "&reblog-key=" + rkey


///////////////////////////////////////////////
// RESULT PROCESSING

// after getPosts
let processPosts postsXML =
   let doc = new XmlDocument()
   postsXML |> doc.LoadXml // so doc is mutable?

   // add prettier printing
   //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

   // process the data
   let tumblr = doc.ChildNodes.Item(1)
   let posts  = tumblr.ChildNodes.Item(1)

   // overall statistics
   let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
   let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
   let num   = posts.ChildNodes.Count

   // posts.HasChildNodes
   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      printfn "id: %s, %s, %s -> %s" id reblogkey date pic

   // print stats
   printfn "%d of %d, starting at %d" num total start

   // print all
   postsFound |> List.map display |> ignore

   (start, total, postsFound)


///////////////////////////////////////////////
// TEST

let test() = 
   // read some posts
   let (start, total, posts) = readPosts 6666 4 |> processPosts

   // parses a datestring into integer (year, month)
   let ym (datestring:string) = 
      // e.g., "2010-11-24 05:57:26 GMT"
      // ignore everything after the yyyy-MM-dd
      let datetime = System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )
      (datetime.Year, datetime.Month)

   // reblog those and delete original posts
   (posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         deletePost id        |> ignore
         ym datestring))


///////////////////////////////////////////////
// agents

type Message = 
   | Message0 of int * int
   
let reader = 
   MailboxProcessor.Start(
      fun inbox ->
         let rec loop() = 
            inbox.Scan(
               function 
               | Message0 (start, count) -> 
                  Some(async { 
                                 readPosts start count 
                                 |> processPosts 
                                 |> ignore
                                 return! loop() 
                  })
               //| _ -> None
            )
         loop()
      )

reader.Post(Message0 (1, 5))
